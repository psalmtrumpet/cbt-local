/**
 * NCS CBT - Student Exam Page JavaScript
 * Timer driven by Unix end-time timestamp for accuracy across page navigations.
 */

(function () {
    'use strict';

    // ── Server data ────────────────────────────────────────────────────────────
    const EXAM        = window.EXAM;
    const sessionId   = EXAM.sessionId;
    const questionId  = EXAM.currentQuestionId;
    const currentIdx  = EXAM.currentIndex;
    const total       = EXAM.totalQuestions;
    const endTimeMs   = EXAM.endTimeMs;
    const answeredSet = new Set(EXAM.answeredQuestionIds);

    // Server tells us exactly how many seconds remain at page-load time.
    // We count down from that using client elapsed time so a wrong device
    // clock cannot cause an instant zero and premature auto-submit.
    const serverRemainingMs = (EXAM.remainingSeconds || 0) * 1000;
    const pageLoadedAt      = Date.now();

    let isSaving      = false;
    let timerHandle   = null;

    // ── Init ───────────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        startTimer();
        initOptions();
        initTheoryAnswer();
        initSubmitButton();
        updateNavAnsweredCount();
    });

    // ─────────────────────────────────────────────────────────────────────────
    // TIMER
    // Uses Date.now() vs the server-supplied Unix end-time so it stays accurate
    // across question navigation (each navigation is a full page reload).
    // ─────────────────────────────────────────────────────────────────────────
    function startTimer() {
        tick(); // run immediately so there's no 1-second blank
    }

    function tick() {
        const elapsed   = Date.now() - pageLoadedAt;
        const remaining = Math.max(0, Math.round((serverRemainingMs - elapsed) / 1000));
        renderTimer(remaining);

        if (remaining <= 0) {
            triggerTimeUp();
            return;
        }
        // Schedule next tick in ~500 ms for smooth display
        timerHandle = setTimeout(tick, 500);
    }

    function renderTimer(seconds) {
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        const text = String(mins).padStart(2, '0') + ':' + String(secs).padStart(2, '0');

        const display = document.getElementById('timerDisplay');
        const label   = document.getElementById('timerText');
        if (!display || !label) return;

        label.textContent = text;

        display.classList.remove('timer-warning', 'timer-danger');
        if (seconds <= 60) {
            display.classList.add('timer-danger');
        } else if (seconds <= 300) {
            display.classList.add('timer-warning');
        }
    }

    function triggerTimeUp() {
        if (timerHandle) clearTimeout(timerHandle);
        renderTimer(0);

        const modal = new bootstrap.Modal(document.getElementById('timeUpModal'));
        modal.show();

        setTimeout(function () {
            document.getElementById('autoSubmitForm').submit();
        }, 2000);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ANSWER OPTIONS
    // ─────────────────────────────────────────────────────────────────────────
    function initOptions() {
        document.querySelectorAll('.option-item').forEach(function (item) {
            item.addEventListener('click', function () {
                const radio = this.querySelector('.option-radio');
                radio.checked = true;
                selectOption(this, radio.value);
            });
        });
    }

    function selectOption(element, value) {
        // Update UI immediately
        document.querySelectorAll('.option-item').forEach(function (o) {
            o.classList.remove('option-selected');
        });
        element.classList.add('option-selected');

        // Persist via AJAX
        saveAnswer(questionId, value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // THEORY ANSWER
    // ─────────────────────────────────────────────────────────────────────────
    function initTheoryAnswer() {
        const textarea = document.getElementById('theoryAnswer');
        if (!textarea) return;

        const counter = document.getElementById('theoryCharCount');
        let saveTimeout = null;

        function updateCount() {
            if (counter) counter.textContent = textarea.value.length;
        }
        updateCount();

        textarea.addEventListener('input', function () {
            updateCount();
            // Auto-save 1.5s after user stops typing
            clearTimeout(saveTimeout);
            saveTimeout = setTimeout(function () {
                saveTheoryAnswer(questionId, textarea.value);
            }, 1500);
        });

        // Save on blur (navigate away)
        textarea.addEventListener('blur', function () {
            clearTimeout(saveTimeout);
            saveTheoryAnswer(questionId, textarea.value);
        });
    }

    function saveTheoryAnswer(qId, text) {
        if (!text.trim()) return;
        fetch('/Student/SaveAnswer', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: sessionId,
                questionId: qId,
                theoryAnswer: text
            })
        })
        .then(r => r.json())
        .then(function (data) {
            if (data.success) {
                answeredSet.add(qId);
                markNavButtonAnswered(qId);
                updateNavAnsweredCount();
            }
        })
        .catch(function () {});
    }

    function saveAnswer(qId, option) {
        if (isSaving) return;
        isSaving = true;

        fetch('/Student/SaveAnswer', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: sessionId,
                questionId: qId,
                selectedOption: option
            })
        })
        .then(function (res) { return res.json(); })
        .then(function (data) {
            isSaving = false;
            if (data.expired) {
                triggerTimeUp();
                return;
            }
            if (data.success) {
                answeredSet.add(qId);
                markNavButtonAnswered(qId);
                updateNavAnsweredCount();
                flashSaved();
            }
        })
        .catch(function () {
            isSaving = false;
        });
    }

    function flashSaved() {
        document.querySelectorAll('.option-item.option-selected').forEach(function (o) {
            o.classList.add('option-saving');
            setTimeout(function () { o.classList.remove('option-saving'); }, 600);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NAVIGATOR
    // ─────────────────────────────────────────────────────────────────────────
    function markNavButtonAnswered(qId) {
        document.querySelectorAll('.q-nav-btn').forEach(function (btn) {
            if (parseInt(btn.dataset.questionId) === qId) {
                btn.classList.add('q-answered');
            }
        });
    }

    function updateNavAnsweredCount() {
        const el = document.getElementById('answeredCount');
        if (el) el.textContent = answeredSet.size;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUBMIT
    // ─────────────────────────────────────────────────────────────────────────
    function initSubmitButton() {
        const btn = document.getElementById('submitExamBtn');
        if (btn) {
            btn.addEventListener('click', showSubmitModal);
        }
    }

    function showSubmitModal() {
        const answered   = answeredSet.size;
        const unanswered = total - answered;

        document.getElementById('modalAnswered').textContent  = answered + ' / ' + total;
        document.getElementById('modalUnanswered').textContent = unanswered;

        const warning = document.getElementById('unansweredWarning');
        document.getElementById('warnCount').textContent = unanswered;
        warning.style.display = unanswered > 0 ? 'block' : 'none';

        new bootstrap.Modal(document.getElementById('submitModal')).show();
    }

})();
