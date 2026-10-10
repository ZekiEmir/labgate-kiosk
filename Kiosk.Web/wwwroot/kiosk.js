document.querySelectorAll('time').forEach(t => {
  const date = new Date(t.dateTime);
  if (!Number.isNaN(date.getTime())) t.textContent = date.toLocaleString('tr-TR');
});
const form = document.querySelector('#reader');
const result = document.querySelector('#result');
document.querySelectorAll('.quick-cards [data-uid]').forEach(button => button.addEventListener('click', () => {
  const input = document.querySelector('#uid');
  input.value = button.dataset.uid;
  input.focus();
}));
form?.addEventListener('submit', async e => {
  e.preventDefault();
  const buttons = document.querySelectorAll('#reader button, .quick-cards button');
  const submit = form.querySelector('[type=submit]');
  const original = submit.innerHTML;
  const request = new FormData(form);
  buttons.forEach(b => b.disabled = true);
  submit.textContent = 'Kart okunuyor…';
  result.className = 'result-box pending';
  result.textContent = 'Kart kontrol ediliyor. Lütfen bekleyin.';
  result.setAttribute('aria-busy', 'true');
  try {
    const response = await fetch(form.action, {method: 'POST', body: request});
    const data = await response.json().catch(() => { throw new Error('Sunucu yanıtı okunamadı. Sayfayı yenileyip tekrar deneyin.'); });
    if (!response.ok) throw new Error(data.status || 'İstek reddedildi. Sayfayı yenileyip tekrar deneyin.');
    const success = data.isSimulation === true && data.simulatedAccessGranted === true;
    result.className = 'result-box ' + (success ? 'success' : 'error');
    result.textContent = `${data.status} · Sanal LED: ${success ? 'yeşil' : 'kırmızı'}. Gerçek erişim: RED.`;
  } catch (error) {
    result.className = 'result-box error';
    result.textContent = `İşlem tamamlanamadı: ${error.message}`;
  } finally {
    buttons.forEach(b => b.disabled = false);
    submit.innerHTML = original;
    result.removeAttribute('aria-busy');
  }
});
