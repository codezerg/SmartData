const backupUpload = (() => {
    const CHUNK_SIZE = 1024 * 1024; // 1 MB
    let abortController = null;
    let currentFile = null;
    let currentBackupId = '';
    let currentOffset = 0;

    function $(id) { return document.getElementById(id); }

    function selectFile(file) {
        if (!file) return;
        currentFile = file;
        currentBackupId = '';
        currentOffset = 0;
        startUpload();
    }

    async function startUpload() {
        abortController = new AbortController();
        showProgress();
        $('upload-filename').textContent = currentFile.name;

        try {
            while (currentOffset < currentFile.size) {
                const chunk = currentFile.slice(currentOffset, currentOffset + CHUNK_SIZE);
                const base64 = await readAsBase64(chunk);

                const resp = await fetch('/console/backups/upload', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        backupId: currentBackupId,
                        data: base64,
                        offset: currentOffset,
                        totalSize: currentFile.size
                    }),
                    signal: abortController.signal
                });

                if (!resp.ok) {
                    let msg = `Upload failed (${resp.status})`;
                    try {
                        const body = await resp.json();
                        if (body.error) msg = body.error;
                    } catch { /* use default msg */ }
                    throw new Error(msg);
                }

                const result = await resp.json();
                currentBackupId = result.backupId;
                currentOffset = result.offset;

                updateProgress(currentOffset, currentFile.size);

                if (result.done) {
                    showResult(true, `Upload complete: ${currentBackupId}`);
                    refreshBackupList();
                    return;
                }
            }
        } catch (err) {
            if (err.name === 'AbortError') {
                showResult(false, 'Upload cancelled.');
            } else {
                showResult(false, err.message, true);
            }
        }
    }

    async function retry() {
        if (!currentFile) return;
        // Resume from last successful offset
        startUpload();
    }

    function cancel() {
        if (abortController) {
            abortController.abort();
            abortController = null;
        }
    }

    function reset() {
        cancel();
        currentFile = null;
        currentBackupId = '';
        currentOffset = 0;
        $('upload-picker').classList.remove('hidden');
        $('upload-progress').classList.add('hidden');
        $('upload-result').classList.add('hidden');
        // Reset the file input
        const input = $('upload-picker').querySelector('input[type="file"]');
        if (input) input.value = '';
    }

    function showProgress() {
        $('upload-picker').classList.add('hidden');
        $('upload-progress').classList.remove('hidden');
        $('upload-result').classList.add('hidden');
        $('upload-bar').style.width = '0%';
        $('upload-status').textContent = 'Uploading...';
        $('upload-icon').textContent = 'cloud_upload';
        $('upload-icon').classList.add('animate-pulse');
        $('upload-cancel-btn').classList.remove('hidden');
    }

    function updateProgress(offset, total) {
        const pct = Math.round((offset / total) * 100);
        $('upload-bar').style.width = pct + '%';
        $('upload-stats').textContent = `${formatSize(offset)} / ${formatSize(total)} (${pct}%)`;
        $('upload-status').textContent = `Uploading... ${pct}%`;
    }

    function showResult(success, message, canRetry) {
        $('upload-progress').classList.add('hidden');
        $('upload-result').classList.remove('hidden');

        const type = success ? 'success' : 'error';
        const icon = success ? 'check_circle' : 'error';
        const css = success
            ? 'bg-green-50 border border-green-200 text-green-700'
            : 'bg-red-50 border border-red-200 text-red-700';

        let html = `<div class="${css} px-4 py-3 rounded-sm text-sm flex items-center">
            <span class="material-symbols-outlined text-[18px] mr-2">${icon}</span>
            ${escapeHtml(message)}`;

        if (canRetry) {
            html += ` <button onclick="backupUpload.retry()" class="ml-3 underline font-semibold">Retry</button>`;
        }

        html += `</div>`;
        $('upload-result').innerHTML = html;
    }

    function refreshBackupList() {
        // Use HTMX to reload the backup list
        const content = document.getElementById('content');
        if (content) {
            htmx.ajax('GET', '/console/backups', { target: '#content', swap: 'innerHTML' });
        }
    }

    function readAsBase64(blob) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => {
                // Strip the data:...;base64, prefix
                const base64 = reader.result.split(',')[1];
                resolve(base64);
            };
            reader.onerror = () => reject(reader.error);
            reader.readAsDataURL(blob);
        });
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    return { selectFile, cancel, reset, retry };
})();
