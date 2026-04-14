function categoryCheckboxes() {
  return document.querySelectorAll('.category-checkboxes input[type="checkbox"]');
}
function selectAllCategories() {
  categoryCheckboxes().forEach(function(cb) { cb.checked = true; });
}
function deselectAllCategories() {
  categoryCheckboxes().forEach(function(cb) { cb.checked = false; });
}
function invertCategories() {
  categoryCheckboxes().forEach(function(cb) { cb.checked = !cb.checked; });
}
function selectPreset(btn) {
  var ids = btn.dataset.catIds ? btn.dataset.catIds.split(',') : [];
  categoryCheckboxes().forEach(function(cb) { cb.checked = ids.includes(cb.value); });
  document.querySelectorAll('.preset-tile').forEach(function(t) { t.classList.remove('selected'); });
  btn.classList.add('selected');
}
