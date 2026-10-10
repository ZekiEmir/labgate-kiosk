const result = document.querySelector('#face-result');
function updateReference(uid, enrolled) {
  const normalized = uid.replace(/[ :\-]/g, '').toUpperCase();
  document.querySelectorAll('[data-user-uid]').forEach(row => {
    if (row.dataset.userUid !== normalized) return;
    const state = row.querySelector('.face-state');
    state.className = 'pill face-state ' + (enrolled ? 'green-pill' : 'muted');
    state.textContent = enrolled ? 'Kayıtlı' : 'Kayıt yok';
  });
  const total = document.querySelectorAll('[data-user-uid]').length;
  const enrolledCount = document.querySelectorAll('.face-state.green-pill').length;
  document.querySelector('#face-count').textContent = `${enrolledCount} / ${total}`;
}
document.querySelectorAll('.select-card').forEach(button => button.addEventListener('click', () => {
  document.querySelector('#enroll-uid').value = button.dataset.uid;
  document.querySelector('#verify-uid').value = button.dataset.uid;
  document.querySelector('#verify-uid').focus();
}));
document.querySelectorAll('.face-form').forEach(form => form.addEventListener('submit', async e => {
  e.preventDefault();
  if (form.dataset.operation === 'delete' && !window.confirm('Bu kartın referans yüz kaydı silinsin mi? Kullanıcı ve kart kaydı korunur.')) return;
  const buttons = document.querySelectorAll('.face-form button, .select-card');
  const submit = form.querySelector('[type=submit]');
  const original = submit.innerHTML;
  const request = new FormData(form);
  buttons.forEach(b => b.disabled = true);
  submit.textContent = 'İşlem sürüyor…';
  result.className = 'result-box pending';
  result.textContent = 'Kamera işlemi sürüyor. Kamerada yalnız bir kişi bulunsun. En fazla 30 saniye bekleyin.';
  result.setAttribute('aria-busy', 'true');
  try {
    const response = await fetch(form.action, {method: 'POST', body: request});
    const data = await response.json().catch(() => { throw new Error('Yanıt okunamadı. Sayfayı yenileyip tekrar deneyin.'); });
    if (!response.ok) throw new Error(data.status || 'İşlem reddedildi. Sayfayı yenileyip tekrar deneyin.');
    const rejectedTest = data.isSimulation === true && data.simulatedAccessGranted !== true;
    result.className = 'result-box ' + (rejectedTest ? 'error' : 'success');
    result.textContent = data.status + (data.isSimulation ? ' · Gerçek erişim: RED' : '');
    if (data.image) {
      const image = document.querySelector('#camera-image');
      image.src = 'data:image/jpeg;base64,' + data.image;
      image.hidden = false;
      document.querySelector('#camera-placeholder').hidden = true;
      document.querySelector('#camera-caption').textContent = 'Son görüntü: ' + new Date().toLocaleTimeString('tr-TR') + '. Referans kaydı ve doğrulama yeni görüntü alır.';
      image.scrollIntoView({behavior: 'smooth', block: 'center'});
    } else {
      if (form.dataset.operation === 'enroll') updateReference(request.get('uid'), true);
      if (form.dataset.operation === 'delete') updateReference(request.get('uid'), false);
      result.focus({preventScroll: true});
      result.scrollIntoView({behavior: 'smooth', block: 'center'});
    }
    const key = form.querySelector('input[name=adminKey]');
    if (key) key.value = '';
  } catch (error) {
    result.className = 'result-box error';
    result.textContent = error.message;
    result.focus({preventScroll: true});
    result.scrollIntoView({behavior: 'smooth', block: 'center'});
  } finally {
    buttons.forEach(b => b.disabled = false);
    submit.innerHTML = original;
    result.removeAttribute('aria-busy');
  }
}));
