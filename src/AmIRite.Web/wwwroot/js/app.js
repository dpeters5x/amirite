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

// Feedback panel
function openFeedback(btn) {
  const card = btn.closest('.question-card');
  const existing = card.querySelector('.feedback-panel');
  if (existing) { existing.remove(); btn.textContent = 'Give feedback'; return; }

  btn.textContent = 'Cancel';

  const token = document.querySelector('input[name="token"]')?.value
    || window.location.pathname.split('/').pop();

  const panel = document.createElement('div');
  panel.className = 'feedback-panel';
  panel.innerHTML = `
    <div class="feedback-panel-inner">
      <p class="feedback-title">Question feedback</p>
      <label class="checkbox-label"><input type="checkbox" name="flag_inappropriate" value="1"> Flag as inappropriate</label>
      <label class="checkbox-label"><input type="checkbox" name="flag_duplicate" value="1"> Flag as duplicate</label>
      <label class="checkbox-label"><input type="checkbox" name="flag_poor_decoys" value="1"> Decoys were too obvious</label>
      <div class="form-group" style="margin-top:0.5rem">
        <label style="font-size:0.8rem;font-weight:700">Quality rating</label>
        <select name="quality_rating" class="input input-sm" style="width:auto">
          <option value="">— no rating —</option>
          <option value="5">5 – Great question</option>
          <option value="4">4 – Good</option>
          <option value="3">3 – OK</option>
          <option value="2">2 – Weak</option>
          <option value="1">1 – Poor</option>
        </select>
      </div>
      <div class="form-group">
        <label style="font-size:0.8rem;font-weight:700">Notes (optional)</label>
        <textarea name="notes" class="input textarea" rows="2" placeholder="Anything else?"></textarea>
      </div>
      <button type="button" class="btn btn-sm btn-primary" onclick="submitFeedback(this)">Submit</button>
      <p class="feedback-msg" style="display:none;margin-top:0.5rem;font-size:0.85rem"></p>
    </div>`;

  panel.dataset.rqId = btn.dataset.rqId;
  panel.dataset.questionId = btn.dataset.questionId;
  panel.dataset.phase = btn.dataset.phase;
  panel.dataset.token = token;

  card.appendChild(panel);
}

function submitFeedback(submitBtn) {
  const panel = submitBtn.closest('.feedback-panel');
  const card = panel.closest('.question-card');
  const feedbackBtn = card.querySelector('.feedback-btn');
  const msg = panel.querySelector('.feedback-msg');

  const body = new FormData();
  body.append('token', panel.dataset.token);
  body.append('rq_id', panel.dataset.rqId);
  body.append('question_id', panel.dataset.questionId);
  body.append('phase', panel.dataset.phase);
  panel.querySelectorAll('input[type="checkbox"]').forEach(cb => {
    if (cb.checked) body.append(cb.name, '1');
  });
  const rating = panel.querySelector('select[name="quality_rating"]').value;
  if (rating) body.append('quality_rating', rating);
  const notes = panel.querySelector('textarea[name="notes"]').value.trim();
  if (notes) body.append('notes', notes);

  submitBtn.disabled = true;
  fetch('/api/question/feedback', { method: 'POST', body })
    .then(r => r.json())
    .then(() => {
      msg.style.display = 'block';
      msg.style.color = 'var(--color-correct)';
      msg.textContent = 'Thanks for the feedback!';
      setTimeout(() => { panel.remove(); feedbackBtn.textContent = 'Give feedback'; }, 1500);
    })
    .catch(() => {
      msg.style.display = 'block';
      msg.style.color = 'var(--color-incorrect)';
      msg.textContent = 'Something went wrong. Please try again.';
      submitBtn.disabled = false;
    });
}

// Initialize theme icon on load
(function () {
  const theme = document.documentElement.getAttribute('data-theme');
  document.querySelectorAll('.theme-icon').forEach(el => {
    el.textContent = theme === 'dark' ? '\u263D' : '\u2600';
  });
})();
