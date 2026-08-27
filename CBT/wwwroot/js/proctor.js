/**
 * NCS CBT — Proctoring Module
 * Face detection, audio+video recording, violation reporting.
 * Only runs on the student exam page.
 */

(function () {
    'use strict';

    const EXAM = window.EXAM;
    const MODEL_URL = '/face-api-weights';

    // ── State ────────────────────────────────────────────────────────────────
    let videoEl, canvasEl, stream;
    let mediaRecorder = null;
    let recordingChunks = [];
    let chunkUploadInterval = null;
    let detectionInterval = null;
    let noFaceConsecutive = 0;
    let lookingAwayConsecutive = 0;

    // Violation cooldown — prevents same type spamming
    const cooldowns = {};
    const COOLDOWN_MS = 20000; // 20s between same type

    // Track total violations this session (synced with server response)
    let localViolationCount = 0;
    let disqualified = false;

    // ── Bootstrap ────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', async function () {
        renderProctoringUI();

        // Tab/window switch detection never depends on camera — start immediately
        watchTabSwitch();

        // ── Step 1: Camera ───────────────────────────────────────────────────
        try {
            await startCamera();
        } catch (err) {
            const msg = getCameraErrorMessage(err);
            console.warn('[Proctor] Camera failed:', err);
            setStatus('error', msg);
            // No camera = no face detection or recording, but exam continues
            return;
        }

        // ── Step 2: Face-api models ──────────────────────────────────────────
        if (typeof faceapi === 'undefined') {
            console.warn('[Proctor] face-api.js not loaded (CDN blocked?).');
            setStatus('warning', 'Detection limited');
            // Recording still works even without face detection
            startRecording();
            return;
        }

        let modelsLoaded = false;
        try {
            await loadFaceModels();
            modelsLoaded = true;
        } catch (err) {
            console.warn('[Proctor] Model load failed:', err);
            setStatus('warning', 'Detection limited');
        }

        // ── Step 3: Recording + detection ───────────────────────────────────
        startRecording();
        if (modelsLoaded) {
            startFaceDetection();
            setStatus('active', 'Monitored');
        }
    });

    // ── UI Injection ─────────────────────────────────────────────────────────
    function renderProctoringUI() {
        const html = `
        <div id="proctorBox">
            <div id="proctorVideoWrapper">
                <video id="proctorVideo" autoplay muted playsinline></video>
                <canvas id="proctorCanvas" style="display:none;"></canvas>
            </div>
            <div id="proctorStatus" class="proctor-status">
                <span id="proctorStatusDot" class="proctor-dot proctor-dot-loading"></span>
                <span id="proctorStatusText">Initialising...</span>
            </div>
        </div>
        <div id="proctorAlert" style="display:none;"></div>`;

        const box = document.createElement('div');
        box.innerHTML = html;
        document.body.appendChild(box);
    }

    // ── Face-api models ───────────────────────────────────────────────────────
    async function loadFaceModels() {
        await Promise.all([
            faceapi.nets.tinyFaceDetector.loadFromUri(MODEL_URL),
            faceapi.nets.faceLandmark68TinyNet.loadFromUri(MODEL_URL)
        ]);
    }

    // ── Camera ────────────────────────────────────────────────────────────────
    async function startCamera() {
        if (!window.isSecureContext || !navigator.mediaDevices) {
            throw new DOMException('Secure context required', 'NotAllowedError');
        }
        stream = await navigator.mediaDevices.getUserMedia({
            video: { width: 320, height: 240, facingMode: 'user' },
            audio: true   // audio included for recording
        });
        videoEl = document.getElementById('proctorVideo');
        canvasEl = document.getElementById('proctorCanvas');
        videoEl.srcObject = stream;
        await videoEl.play();
    }

    function getCameraErrorMessage(err) {
        if (!window.isSecureContext) return 'HTTPS required for camera';
        if (!navigator.mediaDevices) return 'Camera not supported';
        const name = err && err.name;
        if (name === 'NotAllowedError' || name === 'PermissionDeniedError')
            return 'Allow camera access';
        if (name === 'NotFoundError' || name === 'DevicesNotFoundError')
            return 'No camera found';
        if (name === 'NotReadableError' || name === 'TrackStartError')
            return 'Camera in use';
        return 'Camera unavailable';
    }

    // ── Audio + Video Recording ───────────────────────────────────────────────
    function startRecording() {
        if (!stream) return;

        const mimeType = getSupportedMimeType();
        if (!mimeType) {
            console.warn('[Proctor] No supported recording format found.');
            return;
        }

        mediaRecorder = new MediaRecorder(stream, { mimeType });

        mediaRecorder.ondataavailable = function (e) {
            if (e.data && e.data.size > 0)
                recordingChunks.push(e.data);
        };

        // Collect data every 30 seconds
        mediaRecorder.start(30000);

        // Upload accumulated chunks every 60 seconds
        chunkUploadInterval = setInterval(flushChunks, 60000);

        // Also flush on page unload (exam submit / navigate away)
        window.addEventListener('beforeunload', flushChunks);
    }

    function getSupportedMimeType() {
        const types = ['video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm'];
        return types.find(t => MediaRecorder.isTypeSupported(t)) || null;
    }

    function flushChunks() {
        if (!recordingChunks.length) return;
        const blob = new Blob(recordingChunks, { type: 'video/webm' });
        recordingChunks = [];

        const formData = new FormData();
        formData.append('sessionId', EXAM.sessionId);
        formData.append('chunk', blob, `chunk_${Date.now()}.webm`);

        fetch('/Proctor/SaveChunk', { method: 'POST', body: formData })
            .catch(() => { }); // silent fail
    }

    // ── Face Detection ────────────────────────────────────────────────────────
    function startFaceDetection() {
        detectionInterval = setInterval(detect, 1500);
    }

    async function detect() {
        if (!videoEl || videoEl.paused || videoEl.readyState < 2 || disqualified) return;

        const opts = new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.3 });
        let detections;
        try {
            detections = await faceapi.detectAllFaces(videoEl, opts).withFaceLandmarks(true);
        } catch (e) {
            return;
        }

        if (detections.length === 0) {
            noFaceConsecutive++;
            if (noFaceConsecutive >= 3) {
                noFaceConsecutive = 0;
                await reportViolation('NO_FACE', true);
                setStatus('danger', 'Face not visible!');
            } else {
                setStatus('warning', 'Face not detected...');
            }
        } else if (detections.length > 1) {
            noFaceConsecutive = 0;
            await reportViolation('MULTIPLE_FACES', true);
            setStatus('danger', 'Multiple faces!');
        } else {
            noFaceConsecutive = 0;
            const landmarks = detections[0].landmarks;

            if (isLookingAway(landmarks)) {
                lookingAwayConsecutive++;
                if (lookingAwayConsecutive >= 5) {
                    lookingAwayConsecutive = 0;
                    await reportViolation('LOOKING_AWAY', true);
                    setStatus('warning', 'Look at screen!');
                } else {
                    setStatus('warning', 'Look at screen!');
                }
            } else if (isLipMoving(landmarks)) {
                lookingAwayConsecutive = 0;
                await reportViolation('LIP_MOVEMENT', true);
                setStatus('warning', 'Lip movement!');
            } else {
                lookingAwayConsecutive = 0;
                setStatus('active', 'Monitored');
            }
        }
    }

    function isLookingAway(landmarks) {
        const pts = landmarks.positions;
        const noseTip  = pts[30];

        // ── Horizontal (left/right) ────────────────────────────────────────
        const faceCenterX = (pts[0].x + pts[16].x) / 2;
        const faceWidth   = pts[16].x - pts[0].x;
        const hRatio = Math.abs(noseTip.x - faceCenterX) / faceWidth;

        // ── Vertical (up/down) ─────────────────────────────────────────────
        // pts[27]=nose bridge (top), pts[8]=chin (bottom)
        const faceCenterY = (pts[27].y + pts[8].y) / 2;
        const faceHeight  = Math.abs(pts[8].y - pts[27].y);
        const vRatio = (noseTip.y - faceCenterY) / faceHeight; // +ve = looking down, -ve = looking up

        // Lenient thresholds:
        //   sideways  > 0.60  (generous — slight turns are fine)
        //   looking up > -0.30 (significant upward tilt needed)
        //   looking down > 0.45 (moderate downward tilt — more lenient)
        return hRatio > 0.60 || vRatio < -0.30 || vRatio > 0.45;
    }

    function isLipMoving(landmarks) {
        const pts = landmarks.positions;
        const mouthOpen = Math.abs(pts[62].y - pts[66].y);
        const faceHeight = Math.abs(pts[8].y - pts[27].y);
        return mouthOpen / faceHeight > 0.08;
    }

    // ── Tab / Window Focus + DevTools Detection ───────────────────────────────
    function watchTabSwitch() {
        document.addEventListener('visibilitychange', function () {
            if (document.hidden) reportViolation('TAB_SWITCH', false);
        });
        window.addEventListener('blur', function () {
            reportViolation('TAB_SWITCH', false);
        });

        // DevTools detection — triggers if docked DevTools panel opens
        // (size difference between outer and inner window exceeds threshold)
        const DEVTOOLS_THRESHOLD = 200;
        let devToolsOpen = false;
        setInterval(function () {
            const widthDiff = window.outerWidth - window.innerWidth;
            const heightDiff = window.outerHeight - window.innerHeight;
            const isOpen = widthDiff > DEVTOOLS_THRESHOLD || heightDiff > DEVTOOLS_THRESHOLD;
            if (isOpen && !devToolsOpen) {
                devToolsOpen = true;
                reportViolation('TAB_SWITCH', false);
            } else if (!isOpen) {
                devToolsOpen = false;
            }
        }, 1500);

        // Disable right-click and common keyboard shortcuts on exam page
        document.addEventListener('contextmenu', function (e) { e.preventDefault(); });
        document.addEventListener('keydown', function (e) {
            // Block F12, Ctrl+Shift+I, Ctrl+Shift+J, Ctrl+U (view source)
            if (e.key === 'F12') { e.preventDefault(); return; }
            if (e.ctrlKey && e.shiftKey && (e.key === 'I' || e.key === 'J' || e.key === 'C')) {
                e.preventDefault();
            }
            if (e.ctrlKey && e.key === 'u') { e.preventDefault(); }
        });
    }

    // ── Report Violation ──────────────────────────────────────────────────────
    async function reportViolation(type, withSnapshot) {
        if (disqualified) return;

        // Cooldown check
        const now = Date.now();
        if (cooldowns[type] && now - cooldowns[type] < COOLDOWN_MS) return;
        cooldowns[type] = now;

        const snapshot = withSnapshot ? captureSnapshot() : null;

        let response;
        try {
            const res = await fetch('/Proctor/LogViolation', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sessionId: EXAM.sessionId,
                    violationType: type,
                    snapshotBase64: snapshot
                })
            });
            response = await res.json();
        } catch (e) {
            return; // network error — don't crash exam
        }

        if (!response.success) return;

        localViolationCount = response.violationCount;

        if (response.action === 'disqualify') {
            disqualified = true;
            if (chunkUploadInterval) clearInterval(chunkUploadInterval);
            flushChunks();
            showDisqualifyModal(response.redirectUrl);
        }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────
    function captureSnapshot() {
        if (!videoEl || !canvasEl) return null;
        try {
            canvasEl.width = videoEl.videoWidth || 320;
            canvasEl.height = videoEl.videoHeight || 240;
            canvasEl.getContext('2d').drawImage(videoEl, 0, 0);
            return canvasEl.toDataURL('image/jpeg', 0.7).split(',')[1];
        } catch (e) {
            return null;
        }
    }

    // ── Modals ────────────────────────────────────────────────────────────────
    function showDisqualifyModal(redirectUrl) {
        if (detectionInterval) clearInterval(detectionInterval);

        const modalHtml = `
        <div class="modal fade show d-block" id="disqModal" tabindex="-1" style="background:rgba(0,0,0,0.8);" data-bs-backdrop="static">
          <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content border-0 shadow-lg">
              <div class="modal-header bg-danger border-0">
                <h5 class="modal-title fw-bold text-white">
                  <i class="bi bi-shield-x me-2"></i>Disqualified
                </h5>
              </div>
              <div class="modal-body text-center py-5">
                <div class="fs-1 mb-3">🚫</div>
                <h4 class="fw-bold text-danger">You Have Been Disqualified</h4>
                <p class="text-muted">You committed a second proctoring violation. Your exam has been automatically submitted and your candidature flagged.</p>
                <p class="text-muted small">Please contact the examination administrator.</p>
              </div>
              <div class="modal-footer border-0 justify-content-center">
                <a href="${redirectUrl || '/Account/StudentLogin'}" class="btn btn-danger fw-bold px-5">
                  Exit Exam
                </a>
              </div>
            </div>
          </div>
        </div>`;

        const div = document.createElement('div');
        div.innerHTML = modalHtml;
        document.body.appendChild(div.firstElementChild);
    }

    // ── Status Indicator ──────────────────────────────────────────────────────
    function setStatus(state, text) {
        const dot = document.getElementById('proctorStatusDot');
        const label = document.getElementById('proctorStatusText');
        const wrapper = document.getElementById('proctorVideoWrapper');
        if (!dot || !label) return;

        dot.className = 'proctor-dot proctor-dot-' + state;
        label.textContent = text;
        if (wrapper) {
            wrapper.className = '';
            wrapper.classList.add('proctor-video-wrapper', 'wrapper-' + state);
        }
    }

})();
