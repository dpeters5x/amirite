// Theme toggle
function toggleTheme() {
  const current = document.documentElement.getAttribute('data-theme');
  const next = current === 'dark' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  localStorage.setItem('theme', next);
  document.querySelectorAll('.theme-icon').forEach(el => {
    el.textContent = next === 'dark' ? '\u263D' : '\u2600';
  });
}

// Preset tile selection (join page)
function selectPreset(btn) {
  // Deselect all presets
  document.querySelectorAll('.preset-tile').forEach(t => t.classList.remove('selected'));
  btn.classList.add('selected');

  // Check matching categories
  const catIds = (btn.dataset.catIds || '').split(',').map(Number).filter(Boolean);
  document.querySelectorAll('[name="categories"]').forEach(cb => {
    cb.checked = catIds.includes(Number(cb.value));
  });
}

// Bulk broadcast controls
function selectAll() {
  document.querySelectorAll('.recipient-check').forEach(cb => cb.checked = true);
}
function deselectAll() {
  document.querySelectorAll('.recipient-check').forEach(cb => cb.checked = false);
}
function invertSelection() {
  document.querySelectorAll('.recipient-check').forEach(cb => cb.checked = !cb.checked);
}

// Feedback panel (placeholder — expanded when game UI is implemented)
function openFeedback(btn) {
  const rqId = btn.dataset.rqId;
  const phase = btn.dataset.phase;
  // TODO: show inline feedback panel
  console.log('Feedback for rq', rqId, 'phase', phase);
}

// Initialize theme icon on load
(function () {
  const theme = document.documentElement.getAttribute('data-theme');
  document.querySelectorAll('.theme-icon').forEach(el => {
    el.textContent = theme === 'dark' ? '\u263D' : '\u2600';
  });
})();
