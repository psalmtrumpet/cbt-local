/**
 * NCS CBT - Viewer Real-time Dashboard JavaScript
 */

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/examHub')
    .withAutomaticReconnect()
    .build();

// Track timers for each active session
const sessionTimers = {};

// ─── SIGNALR EVENTS ──────────────────────────────────────────────────────────
connection.on('StudentProgressUpdated', function (update) {
    if (update.isSubmitted) return; // handled by StudentSubmitted
    updateOrAddActiveRow(update);
});

connection.on('StudentSubmitted', function (update) {
    // Remove from active table
    removeActiveRow(update.sessionId);
    // Add to completed table
    addCompletedRow(update);
    // Update counts
    updateCounts();
});

connection.on('ViolationDetected', function (data) {
    addViolationAlert(data);
});

connection.on('StudentDisqualified', function (data) {
    // Mark the row in the active table as disqualified and remove after a moment
    const row = document.getElementById(`row-${data.sessionId}`);
    if (row) {
        row.classList.add('table-danger');
        row.querySelector('td:first-child').innerHTML +=
            ' <span class="badge bg-danger ms-1">DISQUALIFIED</span>';
        setTimeout(() => removeActiveRow(data.sessionId), 3000);
    }
    // Add a notice to the violation feed
    addViolationAlert({
        sessionId: data.sessionId,
        studentName: data.studentName,
        studentNumber: data.studentNumber,
        violationType: 'MANUAL_DISQUALIFY',
        violationCount: '—',
        isDisqualified: true,
        timestamp: data.timestamp
    });
});

// ─── CONNECTION ───────────────────────────────────────────────────────────────
connection.start()
    .then(function () {
        connection.invoke('JoinViewerRoom');
        updateConnectionBadge(true);
        // Start client-side timers for existing active rows
        initExistingTimers();
    })
    .catch(function (err) {
        updateConnectionBadge(false);
        console.error('SignalR connection error:', err);
    });

connection.onreconnecting(function () {
    updateConnectionBadge(false);
});

connection.onreconnected(function () {
    connection.invoke('JoinViewerRoom');
    updateConnectionBadge(true);
});

// ─── UI HELPERS ───────────────────────────────────────────────────────────────
function updateConnectionBadge(connected) {
    const badge = document.getElementById('connectionBadge');
    if (!badge) return;
    if (connected) {
        badge.className = 'badge bg-success fs-6';
        badge.innerHTML = '<i class="bi bi-broadcast me-1"></i>Live';
    } else {
        badge.className = 'badge bg-warning text-dark fs-6';
        badge.innerHTML = '<i class="bi bi-broadcast-pin me-1"></i>Reconnecting...';
    }
}

function updateOrAddActiveRow(update) {
    const row = document.getElementById(`row-${update.sessionId}`);
    const progressPct = update.totalQuestions > 0
        ? (update.questionsAnswered / update.totalQuestions * 100)
        : 0;

    if (row) {
        // Update existing row
        const prog = document.getElementById(`prog-${update.sessionId}`);
        const progText = document.getElementById(`progText-${update.sessionId}`);
        const score = document.getElementById(`score-${update.sessionId}`);

        if (prog) prog.style.width = progressPct + '%';
        if (progText) progText.textContent = `${update.questionsAnswered}/${update.totalQuestions}`;
        if (score) score.textContent = `${update.currentScore}/${update.totalQuestions}`;

        // Flash the row
        row.classList.add('row-flash');
        setTimeout(() => row.classList.remove('row-flash'), 800);
    } else {
        // Add new row (student just logged in)
        addActiveRow(update);
    }

    // Hide the empty message if shown
    const noMsg = document.getElementById('noActiveMsg');
    if (noMsg) noMsg.style.display = 'none';
}

function addActiveRow(update) {
    let tbody = document.getElementById('activeTableBody');

    // Create table if it doesn't exist
    if (!tbody) {
        const container = document.getElementById('activeSessionsContainer');
        container.innerHTML = `
            <div class="table-responsive">
                <table class="table table-hover mb-0" id="activeTable">
                    <thead class="table-light">
                        <tr>
                            <th>Student</th>
                            <th>Student No.</th>
                            <th>Exam</th>
                            <th style="min-width:200px;">Progress</th>
                            <th class="text-center">Score</th>
                            <th class="text-center">Time Left</th>
                        </tr>
                    </thead>
                    <tbody id="activeTableBody"></tbody>
                </table>
            </div>`;
        tbody = document.getElementById('activeTableBody');
    }

    const tr = document.createElement('tr');
    tr.id = `row-${update.sessionId}`;
    tr.className = 'row-new';
    tr.innerHTML = `
        <td class="fw-medium">${escHtml(update.studentName)}</td>
        <td><span class="badge bg-secondary">${escHtml(update.studentNumber)}</span></td>
        <td class="text-muted small">${escHtml(update.examTitle)}</td>
        <td>
            <div class="d-flex align-items-center gap-2">
                <div class="progress flex-grow-1" style="height:10px;">
                    <div class="progress-bar bg-ncs progress-bar-striped progress-bar-animated"
                         id="prog-${update.sessionId}" style="width:0%"></div>
                </div>
                <small class="text-nowrap text-ncs fw-bold" id="progText-${update.sessionId}">
                    0/${update.totalQuestions}
                </small>
            </div>
        </td>
        <td class="text-center">
            <span class="badge bg-ncs-light text-ncs fw-bold fs-6" id="score-${update.sessionId}">
                0/${update.totalQuestions}
            </span>
        </td>
        <td class="text-center">
            <span class="timer-badge" id="time-${update.sessionId}" data-remaining="300">
                <i class="bi bi-clock me-1"></i>--:--
            </span>
        </td>`;
    tbody.appendChild(tr);

    setTimeout(() => tr.classList.remove('row-new'), 1000);

    // Fetch fresh data to get actual timer
    refreshLiveData();
}

function removeActiveRow(sessionId) {
    const row = document.getElementById(`row-${sessionId}`);
    if (row) {
        row.classList.add('row-removing');
        setTimeout(() => row.remove(), 600);
    }
    // Clear timer
    if (sessionTimers[sessionId]) {
        clearInterval(sessionTimers[sessionId]);
        delete sessionTimers[sessionId];
    }
}

function addCompletedRow(update) {
    const tbody = document.getElementById('completedTableBody');
    if (!tbody) return;

    const pct = update.totalQuestions > 0
        ? (update.currentScore / update.totalQuestions * 100).toFixed(1)
        : 0;
    const passClass = parseFloat(pct) >= 50 ? 'text-success' : 'text-danger';
    const barClass = parseFloat(pct) >= 70 ? 'bg-success' : parseFloat(pct) >= 50 ? 'bg-warning' : 'bg-danger';
    const now = new Date().toLocaleTimeString();
    const vc = update.violationCount || 0;
    const violBtn = vc > 0
        ? `<button class="btn btn-sm btn-danger" onclick="showViolationsModal(${update.sessionId})">
               <i class="bi bi-shield-exclamation me-1"></i>${vc}
           </button>`
        : `<button class="btn btn-sm btn-outline-secondary" onclick="showViolationsModal(${update.sessionId})">
               <i class="bi bi-shield-check me-1"></i>None
           </button>`;

    const tr = document.createElement('tr');
    tr.className = update.isDisqualified ? 'row-new table-danger' : 'row-new';
    tr.innerHTML = `
        <td class="fw-medium">
            ${escHtml(update.studentName)}
            ${update.isDisqualified ? '<span class="badge bg-danger ms-1">DISQUALIFIED</span>' : ''}
        </td>
        <td><span class="badge bg-secondary">${escHtml(update.studentNumber)}</span></td>
        <td class="text-muted small">${escHtml(update.examTitle)}</td>
        <td class="text-center">
            <strong class="${passClass}">${update.currentScore}/${update.totalQuestions}</strong>
        </td>
        <td>
            <div class="d-flex align-items-center gap-2">
                <div class="progress flex-grow-1" style="height:10px;">
                    <div class="progress-bar ${barClass}" style="width:${pct}%"></div>
                </div>
                <small class="fw-bold text-nowrap ${passClass}">${pct}%</small>
            </div>
        </td>
        <td class="text-muted small">${now}</td>
        <td class="text-center">${violBtn}</td>`;

    tbody.insertBefore(tr, tbody.firstChild);

    const noMsg = document.getElementById('noCompletedMsg');
    if (noMsg) noMsg.style.display = 'none';

    setTimeout(() => tr.classList.remove('row-new'), 1000);
}

function updateCounts() {
    const activeRows = document.querySelectorAll('#activeTableBody tr').length;
    const completedRows = document.querySelectorAll('#completedTableBody tr').length;

    const activeCount = document.getElementById('activeCount');
    const completedCount = document.getElementById('completedCount');
    const activeBadge = document.getElementById('activeBadge');
    const completedBadge = document.getElementById('completedBadge');

    if (activeCount) activeCount.textContent = activeRows;
    if (completedCount) completedCount.textContent = completedRows;
    if (activeBadge) activeBadge.textContent = `${activeRows} active`;
    if (completedBadge) completedBadge.textContent = `${completedRows} completed`;

    if (activeRows === 0) {
        const tbody = document.getElementById('activeTableBody');
        if (tbody && tbody.children.length === 0) {
            const container = document.getElementById('activeSessionsContainer');
            container.innerHTML = `
                <div class="text-center py-5 text-muted" id="noActiveMsg">
                    <i class="bi bi-hourglass fs-1 d-block mb-2"></i>
                    No students are currently taking an exam
                </div>`;
        }
    }
}

// ─── COUNTDOWN TIMERS ─────────────────────────────────────────────────────────
function initExistingTimers() {
    document.querySelectorAll('[id^="time-"]').forEach(function (el) {
        const sessionId = el.id.replace('time-', '');
        let remaining = parseInt(el.dataset.remaining, 10) || 0;
        updateTimerEl(el, remaining);

        sessionTimers[sessionId] = setInterval(function () {
            remaining--;
            updateTimerEl(el, remaining);
            if (remaining <= 0) {
                clearInterval(sessionTimers[sessionId]);
            }
        }, 1000);
    });
}

function updateTimerEl(el, seconds) {
    const mins = Math.floor(Math.max(0, seconds) / 60);
    const secs = Math.max(0, seconds) % 60;
    el.innerHTML = `<i class="bi bi-clock me-1"></i>${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;

    el.className = 'timer-badge';
    if (seconds <= 300 && seconds > 60) el.classList.add('timer-badge-warning');
    else if (seconds <= 60) el.classList.add('timer-badge-danger');
}

// ─── REFRESH ─────────────────────────────────────────────────────────────────
function refreshLiveData() {
    fetch('/Viewer/LiveData')
        .then(r => r.json())
        .then(data => {
            data.forEach(s => {
                const timeEl = document.getElementById(`time-${s.sessionId}`);
                if (timeEl) {
                    // Clear old timer and restart with fresh data
                    if (sessionTimers[s.sessionId]) {
                        clearInterval(sessionTimers[s.sessionId]);
                    }
                    let remaining = s.remainingSeconds;
                    timeEl.dataset.remaining = remaining;
                    updateTimerEl(timeEl, remaining);
                    sessionTimers[s.sessionId] = setInterval(function () {
                        remaining--;
                        updateTimerEl(timeEl, remaining);
                        if (remaining <= 0) {
                            clearInterval(sessionTimers[s.sessionId]);
                        }
                    }, 1000);
                }
            });
        });
}

// Auto-refresh live data every 30 seconds
setInterval(refreshLiveData, 30000);

// ─── VIOLATION ALERTS ────────────────────────────────────────────────────────
const violationLabels = {
    NO_FACE: 'No Face Detected',
    MULTIPLE_FACES: 'Multiple Faces',
    LOOKING_AWAY: 'Looking Away',
    LIP_MOVEMENT: 'Lip Movement',
    TAB_SWITCH: 'Tab Switch',
    MANUAL_DISQUALIFY: 'Manually Disqualified by Proctor'
};

function addViolationAlert(data) {
    const feed = document.getElementById('violationFeed');
    if (!feed) return;

    const label = violationLabels[data.violationType] || data.violationType;
    const isDisq = data.isDisqualified;
    const time = data.timestamp || new Date().toLocaleTimeString();

    // Offence icon + colour
    const iconMap = {
        NO_FACE: 'bi-camera-video-off',
        MULTIPLE_FACES: 'bi-people-fill',
        LOOKING_AWAY: 'bi-eye-slash',
        LIP_MOVEMENT: 'bi-chat-dots',
        TAB_SWITCH: 'bi-window-dash',
        MANUAL_DISQUALIFY: 'bi-slash-circle'
    };
    const colourMap = {
        NO_FACE: 'danger', MULTIPLE_FACES: 'danger',
        LOOKING_AWAY: 'warning', LIP_MOVEMENT: 'warning',
        TAB_SWITCH: 'warning', MANUAL_DISQUALIFY: 'danger'
    };
    const icon   = iconMap[data.violationType]   || 'bi-exclamation-triangle';
    const colour = colourMap[data.violationType] || 'secondary';

    // Snapshot thumbnail — click to open full size
    const snapshotHtml = data.snapshotPath
        ? `<a href="/Proctor/Snapshot?path=${encodeURIComponent(data.snapshotPath)}" target="_blank" title="Click to view full snapshot">
               <img src="/Proctor/Snapshot?path=${encodeURIComponent(data.snapshotPath)}"
                    style="width:90px;height:68px;object-fit:cover;border-radius:6px;border:2px solid #dc3545;flex-shrink:0;"
                    onerror="this.parentElement.innerHTML='<span class=\'text-muted small\'>No image</span>'" />
           </a>`
        : '<span class="text-muted small" style="width:90px;display:inline-block;text-align:center;">No snapshot</span>';

    const disqBtn = (!isDisq && data.sessionId)
        ? `<button class="btn btn-danger btn-sm" onclick="disqualifyStudent(${data.sessionId}, this)">
               <i class="bi bi-slash-circle me-1"></i>Disqualify
           </button>`
        : '<span class="badge bg-danger fs-6">DISQUALIFIED</span>';

    const item = document.createElement('div');
    item.className = `violation-item ${isDisq ? 'violation-disq' : 'violation-warn'} mb-2 p-2`;
    item.innerHTML = `
        <div class="d-flex gap-3 align-items-start">
            ${snapshotHtml}
            <div class="flex-grow-1 min-w-0">
                <div class="d-flex justify-content-between align-items-start flex-wrap gap-1">
                    <div>
                        <strong class="fs-6">${escHtml(data.studentName)}</strong>
                        <span class="badge bg-secondary ms-1">${escHtml(data.studentNumber)}</span>
                    </div>
                    <small class="text-muted"><i class="bi bi-clock me-1"></i>${escHtml(time)}</small>
                </div>
                <div class="mt-1">
                    <span class="badge bg-${colour}">
                        <i class="bi ${icon} me-1"></i>${escHtml(label)}
                    </span>
                    <span class="badge bg-secondary ms-1">Offence #${data.violationCount}</span>
                </div>
                <div class="mt-2">${disqBtn}</div>
            </div>
        </div>`;

    feed.insertBefore(item, feed.firstChild);

    // Keep only last 30 alerts
    while (feed.children.length > 30) feed.removeChild(feed.lastChild);

    // Update violation counter badge
    const badge = document.getElementById('violationCountBadge');
    if (badge) {
        const cur = parseInt(badge.textContent, 10) || 0;
        badge.textContent = cur + 1;
    }
}

function disqualifyStudent(sessionId, btn) {
    if (!confirm('Disqualify this student? Their exam will be submitted immediately and they cannot continue.')) return;
    btn.disabled = true;
    btn.innerHTML = '<i class="bi bi-hourglass me-1"></i>Processing...';

    fetch('/Proctor/DisqualifyStudent', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId })
    })
    .then(r => r.json())
    .then(data => {
        if (data.success) {
            btn.closest('.violation-item').classList.add('violation-disq');
            btn.outerHTML = '<span class="badge bg-danger">DISQUALIFIED</span>';
        } else {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-slash-circle me-1"></i>Disqualify';
            alert(data.message || 'Could not disqualify student.');
        }
    })
    .catch(() => {
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-slash-circle me-1"></i>Disqualify';
    });
}

// ─── VIOLATIONS MODAL ────────────────────────────────────────────────────────
const violationIcons = {
    NO_FACE: 'bi-camera-video-off',
    MULTIPLE_FACES: 'bi-people-fill',
    LOOKING_AWAY: 'bi-eye-slash',
    LIP_MOVEMENT: 'bi-chat-dots',
    TAB_SWITCH: 'bi-window-dash',
    MANUAL_DISQUALIFY: 'bi-slash-circle'
};
const violationColours = {
    NO_FACE: 'danger', MULTIPLE_FACES: 'danger',
    LOOKING_AWAY: 'warning', LIP_MOVEMENT: 'warning',
    TAB_SWITCH: 'warning', MANUAL_DISQUALIFY: 'danger'
};

let _modalSessionId = null;

async function showViolationsModal(sessionId) {
    _modalSessionId = sessionId;
    const modal = new bootstrap.Modal(document.getElementById('violationsModal'));

    document.getElementById('violationsModalTitle').innerHTML =
        '<i class="bi bi-shield-exclamation me-2"></i>Loading violations...';
    document.getElementById('violationsModalSummary').innerHTML =
        '<div class="text-center py-3"><div class="spinner-border spinner-border-sm text-danger"></div> Loading...</div>';
    document.getElementById('violationsModalBody').innerHTML = '';
    document.getElementById('violationsDisqualifyBtn').style.display = 'none';
    modal.show();

    let data;
    try {
        const resp = await fetch(`/Viewer/SessionViolations?sessionId=${sessionId}`);
        if (!resp.ok) throw new Error('Not found');
        data = await resp.json();
    } catch {
        document.getElementById('violationsModalSummary').innerHTML =
            '<p class="text-danger p-3 mb-0">Could not load violations.</p>';
        return;
    }

    const s = data.session;
    document.getElementById('violationsModalTitle').innerHTML =
        `<i class="bi bi-shield-exclamation me-2"></i>${escHtml(s.studentName)} — Violations`;

    // Summary bar
    const pct = s.totalQuestions > 0 ? (s.score / s.totalQuestions * 100).toFixed(1) : 0;
    const disqBadge = s.isDisqualified
        ? '<span class="badge bg-danger ms-2">DISQUALIFIED</span>' : '';
    document.getElementById('violationsModalSummary').innerHTML = `
        <div class="d-flex flex-wrap gap-3 align-items-center">
            <div><span class="text-muted small">Student</span><br>
                <strong>${escHtml(s.studentName)}</strong>
                <span class="badge bg-secondary ms-1">${escHtml(s.studentNumber)}</span>
                ${disqBadge}
            </div>
            <div><span class="text-muted small">Exam</span><br>
                <strong>${escHtml(s.examTitle)}</strong>
            </div>
            <div><span class="text-muted small">Score</span><br>
                <strong>${s.score}/${s.totalQuestions} (${pct}%)</strong>
            </div>
            <div><span class="text-muted small">Violations</span><br>
                <span class="badge bg-${s.violationCount > 0 ? 'danger' : 'success'} fs-6">
                    ${s.violationCount}
                </span>
            </div>
        </div>`;

    // Violation list
    const body = document.getElementById('violationsModalBody');
    if (!data.violations || data.violations.length === 0) {
        body.innerHTML = `
            <div class="text-center py-4 text-muted">
                <i class="bi bi-shield-check fs-2 text-success d-block mb-2"></i>
                No violations recorded for this student.
            </div>`;
    } else {
        body.innerHTML = data.violations.map((v, i) => {
            const label  = violationLabels[v.violationType]  || v.violationType;
            const icon   = violationIcons[v.violationType]   || 'bi-exclamation-triangle';
            const colour = violationColours[v.violationType] || 'secondary';
            const thumb  = v.snapshotPath
                ? `<a href="/Proctor/Snapshot?path=${encodeURIComponent(v.snapshotPath)}" target="_blank" title="View full snapshot">
                       <img src="/Proctor/Snapshot?path=${encodeURIComponent(v.snapshotPath)}"
                            style="width:100px;height:75px;object-fit:cover;border-radius:6px;border:2px solid #dee2e6;flex-shrink:0;"
                            onerror="this.style.display='none'" />
                   </a>`
                : `<div style="width:100px;height:75px;background:#f8f9fa;border-radius:6px;display:flex;align-items:center;justify-content:center;flex-shrink:0;">
                       <i class="bi bi-camera-video-off text-muted fs-4"></i>
                   </div>`;
            return `
                <div class="d-flex gap-3 align-items-start p-3 border-bottom">
                    ${thumb}
                    <div class="flex-grow-1">
                        <div class="d-flex justify-content-between align-items-start">
                            <span class="badge bg-${colour} fs-6 mb-1">
                                <i class="bi ${icon} me-1"></i>${escHtml(label)}
                            </span>
                            <small class="text-muted"><i class="bi bi-clock me-1"></i>${escHtml(v.timestamp)}</small>
                        </div>
                        <div class="text-muted small">Violation #${i + 1}</div>
                    </div>
                </div>`;
        }).join('');
    }

    // Show disqualify button only if not already disqualified
    if (!s.isDisqualified) {
        const btn = document.getElementById('violationsDisqualifyBtn');
        btn.style.display = '';
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-slash-circle me-2"></i>Disqualify Student';
    }
}

async function disqualifyFromModal() {
    if (!_modalSessionId) return;
    if (!confirm('Disqualify this student? This will flag their result and cannot be undone.')) return;

    const btn = document.getElementById('violationsDisqualifyBtn');
    btn.disabled = true;
    btn.innerHTML = '<i class="bi bi-hourglass me-2"></i>Processing...';

    try {
        const resp = await fetch('/Proctor/DisqualifyStudent', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId: _modalSessionId })
        });
        const data = await resp.json();
        if (data.success) {
            btn.style.display = 'none';
            // Update summary bar
            const summary = document.getElementById('violationsModalSummary');
            summary.innerHTML = summary.innerHTML.replace(
                /(<strong>[^<]+<\/strong>\s*<span class="badge bg-secondary[^>]+>[^<]+<\/span>)/,
                '$1 <span class="badge bg-danger ms-2">DISQUALIFIED</span>'
            );
            // Update the row in the completed table if it exists
            document.querySelectorAll('#completedTableBody tr').forEach(row => {
                const btn = row.querySelector(`button[onclick="showViolationsModal(${_modalSessionId})"]`);
                if (btn) {
                    row.classList.add('table-danger');
                    const nameCell = row.querySelector('td:first-child');
                    if (nameCell && !nameCell.querySelector('.badge.bg-danger')) {
                        nameCell.insertAdjacentHTML('beforeend', ' <span class="badge bg-danger">DISQUALIFIED</span>');
                    }
                }
            });
        } else {
            alert(data.message || 'Could not disqualify student.');
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-slash-circle me-2"></i>Disqualify Student';
        }
    } catch {
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-slash-circle me-2"></i>Disqualify Student';
    }
}

// ─── UTILS ───────────────────────────────────────────────────────────────────
function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
