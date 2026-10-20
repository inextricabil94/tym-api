(function (root) {
  'use strict';
  const e = React.createElement;
  const I18n = root.TymI18n;
  const t = I18n.t;
  let googleScript;
  function loadGoogle() {
    if (root.google?.accounts?.id) return Promise.resolve();
    if (!googleScript) googleScript = new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = 'https://accounts.google.com/gsi/client'; script.async = true;
      script.onload = resolve;
      script.onerror = () => { googleScript = null; script.remove(); reject(new Error('Google sign-in could not load.')); };
      document.head.appendChild(script);
    });
    return googleScript;
  }

  function createClient(baseUrl) {
    const base = baseUrl.trim().replace(/\/+$/, '');
    let state = { user: null, config: null, error: '', loading: true };
    let tokens = null, refreshing = null, version = 0;
    const listeners = new Set();
    function update(change) { state = { ...state, ...change }; for (const listener of listeners) listener(); }
    async function request(path, body, token, signal) {
      const response = await fetch(base + path, {
        method: body === undefined ? 'GET' : 'POST', cache: 'no-store', credentials: 'omit', signal,
        headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        ...(body === undefined ? {} : { body: JSON.stringify(body) })
      });
      let data = null;
      if (response.status !== 204) { try { data = await response.json(); } catch {} }
      if (!response.ok) {
        const error = response.status === 429 ? I18n.error('Too many sign-in requests. Please retry later.')
          : I18n.apiError(data, response.status);
        error.status = response.status; throw error;
      }
      return data;
    }
    function readTokens(data) {
      const access = data?.accessToken ?? data?.access_token;
      const refresh = data?.refreshToken ?? data?.refresh_token;
      const seconds = data?.expiresIn ?? data?.expires_in;
      if (!access || !refresh || !Number.isFinite(seconds)) throw new Error('Invalid sign-in response.');
      return { access, refresh, expires: Date.now() + seconds * 1000 };
    }
    async function accept(data, expectedVersion) {
      const next = readTokens(data);
      const user = await request('/v1/account/me', undefined, next.access);
      if (version !== expectedVersion) return;
      tokens = next; update({ user, error: '' });
    }
    async function token() {
      if (!tokens) throw new Error('Sign in to read text.');
      if (tokens.expires > Date.now() + 30000) return tokens.access;
      if (!refreshing) {
        const expected = version;
        refreshing = request('/v1/account/refresh', { refresh_token: tokens.refresh }).then(data => {
          if (version !== expected) throw new Error('Sign-in session changed.');
          tokens = readTokens(data); return tokens.access;
        }).catch(error => { if (version === expected) { tokens = null; update({ user: null, error: 'Session expired. Sign in again.' }); } throw error; })
          .finally(() => { refreshing = null; });
      }
      return refreshing;
    }
    return {
      subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
      snapshot: () => state,
      async initialize(signal) {
        try { const config = await request('/v1/account/config', undefined, null, signal); if (!signal.aborted) update({ config, loading: false }); }
        catch (error) { if (!signal.aborted) update({ error: I18n.describe(error), loading: false }); }
      },
      async login(email, password) { const expected = ++version; await accept(await request('/v1/account/login', { email, password }), expected); },
      register: (email, password) => request('/v1/account/register', { email, password }),
      forgot: email => request('/v1/account/forgotPassword', { email }),
      resend: email => request('/v1/account/resendConfirmationEmail', { email }),
      reset: (email, resetCode, newPassword) => request('/v1/account/resetPassword', { email, reset_code: resetCode, new_password: newPassword }),
      nonce: () => request('/v1/account/google/nonce', {}),
      readPublic: (path, signal) => request(path, undefined, null, signal),
      async google(credential, nonce) { const expected = ++version; await accept(await request('/v1/account/google', { credential, nonce }), expected); },
      async read(path, signal) {
        const expected = version;
        const access = await token();
        if (signal.aborted || expected !== version) throw new DOMException('Aborted', 'AbortError');
        try { return await request(path, undefined, access, signal); }
        catch (error) {
          if ((error.status === 401 || error.status === 403) && expected === version) {
            tokens = null; update({ user: null, error: 'Session expired. Sign in again.' });
          }
          throw error;
        }
      },
      async logout() {
        const access = tokens?.access; ++version; tokens = null; update({ user: null, error: '' });
        if (access) { try { await request('/v1/account/logout', {}, access); } catch { update({ error: 'Signed out here. Server sign-out failed; other sessions may remain active.' }); } }
      },
      dispose() { ++version; tokens = null; listeners.clear(); }
    };
  }

  function useAccount(baseUrl) {
    const client = React.useMemo(() => createClient(baseUrl), [baseUrl]);
    const state = React.useSyncExternalStore(client.subscribe, client.snapshot);
    React.useEffect(() => {
      const controller = new AbortController(); client.initialize(controller.signal);
      return () => { controller.abort(); client.dispose(); };
    }, [client]);
    return { ...state, client };
  }

  function GoogleButton({ client, clientId, onSuccess, onError }) {
    const { locale } = I18n.useLocale();
    const target = React.useRef(null);
    const [attempt, setAttempt] = React.useState(0);
    React.useEffect(() => {
      let active = true;
      (async () => {
        try {
          const challenge = await client.nonce(); await loadGoogle();
          if (!active) return;
          root.google.accounts.id.initialize({ client_id: clientId, nonce: challenge.nonce,
            callback: async result => {
              try { await client.google(result.credential, challenge.nonce); if (active) onSuccess(); }
              catch (error) { if (active) { onError(I18n.describe(error)); setAttempt(value => value + 1); } }
            } });
          target.current.replaceChildren();
          root.google.accounts.id.renderButton(target.current, { theme: 'outline', size: 'large', text: 'continue_with', width: 260, locale });
        } catch (error) { if (active) onError(I18n.describe(error)); }
      })();
      return () => { active = false; root.google?.accounts?.id?.cancel(); };
    }, [client, clientId, attempt, locale]);
    return e('div', { className: 'google-sign-in', ref: target });
  }

  function AccountDialog({ account, onClose }) {
    I18n.useLocale();
    const ref = React.useRef(null);
    const reset = React.useMemo(() => new URLSearchParams(root.location.hash.slice(1)), []);
    const [mode, setMode] = React.useState(reset.has('resetCode') ? 'reset' : 'login');
    const [email, setEmail] = React.useState(reset.get('resetEmail') || '');
    const [password, setPassword] = React.useState('');
    const [confirm, setConfirm] = React.useState('');
    const [busy, setBusy] = React.useState(false);
    const [error, setError] = React.useState('');
    const [message, setMessage] = React.useState('');
    React.useEffect(() => {
      ref.current.showModal();
      if (reset.has('resetCode')) history.replaceState(null, '', root.location.pathname + root.location.search);
      return () => ref.current?.close();
    }, []);
    const title = { login: 'Sign In', register: 'Create Account', forgot: 'Reset Password', reset: 'Set New Password', resend: 'Confirm Email' }[mode];
    function change(next) { setMode(next); setError(''); setMessage(''); setPassword(''); setConfirm(''); }
    async function submit(event) {
      event.preventDefault(); setError(''); setMessage('');
      if ((mode === 'register' || mode === 'reset') && password !== confirm) { setError('Passwords do not match.'); return; }
      setBusy(true);
      try {
        if (mode === 'login') { await account.client.login(email, password); onClose(); }
        else if (mode === 'register') {
          await account.client.register(email, password); change('login');
          setMessage(account.config?.email_delivery === 'local_test'
            ? 'Confirmation saved to local test mail. No email was sent.' : 'Check your email to confirm your account, then sign in.');
        } else if (mode === 'reset') {
          await account.client.reset(email, reset.get('resetCode'), password); change('login'); setMessage('Password updated. You can sign in.');
        } else {
          await account.client[mode](email);
          setMessage(account.config?.email_delivery === 'local_test'
            ? 'If a message was needed, it was saved to local test mail. No email was sent.'
            : mode === 'forgot' ? 'If that account exists, a password-reset email has been sent.' : 'If confirmation is needed, an email has been sent.');
        }
      } catch (failure) { setError(I18n.describe(failure)); }
      finally { setBusy(false); setPassword(''); setConfirm(''); }
    }
    const unavailable = !account.config?.enabled;
    const emailUnavailable = unavailable || !account.config?.email_enabled;
    return e('dialog', { className: 'account-dialog', ref, 'aria-labelledby': 'account-title', onCancel: onClose },
      e('div', { className: 'account-heading' }, e('h2', { id: 'account-title' }, t(title)),
        e('button', { type: 'button', className: 'tool', title: t('Close'), 'aria-label': t('Close sign-in'), onClick: onClose },
          e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, 'aria-hidden': true },
            e('path', { d: 'm18 6-12 12M6 6l12 12' })))),
      mode === 'login' && account.config?.google_client_id ? e(GoogleButton, { client: account.client,
        clientId: account.config.google_client_id, onSuccess: onClose, onError: setError }) : null,
      account.loading ? e('p', { role: 'status' }, t('Loading sign-in...')) : null,
      unavailable ? e('p', { className: 'message', role: 'status' }, t(account.error || 'Sign-in is not configured yet.')) : null,
      !unavailable && emailUnavailable && mode !== 'login' ? e('p', { className: 'message', role: 'status' }, t('Email registration is not available yet.')) : null,
      account.config?.email_delivery === 'local_test' ? e('p', { className: 'account-message', role: 'status' },
        t('Local test mail only. Messages are saved on this computer, not sent to an inbox.')) : null,
      e('form', { onSubmit: submit, className: 'account-form' },
        e('div', { className: 'field' }, e('label', { htmlFor: 'account-email' }, t('Email')),
          e('input', { id: 'account-email', type: 'email', required: true, autoComplete: 'email', maxLength: 254,
            value: email, onChange: event => setEmail(event.target.value), disabled: busy || unavailable })),
        ['login', 'register', 'reset'].includes(mode) ? e('div', { className: 'field' }, e('label', { htmlFor: 'account-password' }, t('Password')),
          e('input', { id: 'account-password', type: 'password', required: true, minLength: mode === 'login' ? 1 : 12,
            maxLength: 128, autoComplete: mode === 'login' ? 'current-password' : 'new-password',
            value: password, onChange: event => setPassword(event.target.value), disabled: busy || unavailable })) : null,
        ['register', 'reset'].includes(mode) ? e('div', { className: 'field' }, e('label', { htmlFor: 'account-confirm' }, t('Confirm Password')),
          e('input', { id: 'account-confirm', type: 'password', required: true, minLength: 12, maxLength: 128, autoComplete: 'new-password',
            value: confirm, onChange: event => setConfirm(event.target.value), disabled: busy || emailUnavailable })) : null,
        error ? e('p', { className: 'message error', role: 'alert' }, t(error)) : null,
        message ? e('p', { className: 'account-message', role: 'status' }, t(message)) : null,
        e('button', { className: 'primary', type: 'submit', disabled: busy || unavailable || (mode !== 'login' && emailUnavailable) },
          t(busy ? 'Please wait...' : { login: 'Sign In With Email', register: 'Create Account', forgot: 'Send Reset Email', reset: 'Save Password', resend: 'Resend Confirmation' }[mode]))),
      e('nav', { className: 'account-links', 'aria-label': t('Account options') },
        (mode === 'login' ? [['register', 'Create account'], ['forgot', 'Forgot password'], ['resend', 'Resend confirmation']]
          : [['login', 'Back to sign in']]).map(([next, label]) => e('button', { key: next, type: 'button', className: 'text-button',
            disabled: busy, onClick: () => change(next) }, t(label))))
    );
  }

  function SourceReader({ account, book, section, language, onSignIn }) {
    const { locale } = I18n.useLocale();
    const [source, setSource] = React.useState(null);
    const [error, setError] = React.useState('');
    const [loading, setLoading] = React.useState(false);
    const key = `${book.id}:${section?.id || 'full'}`;
    const accessKnown = Boolean(account.config);
    const requiresLogin = account.config?.text_requires_login !== false;
    const reader = requiresLogin ? account.user : null;
    React.useEffect(() => {
      const controller = new AbortController(); setSource(null); setError(''); setLoading(false);
      if (accessKnown && (!requiresLogin || reader)) {
        setLoading(true);
        const query = section ? `?sectionId=${encodeURIComponent(section.id)}` : '';
        const read = requiresLogin ? account.client.read : account.client.readPublic;
        read(`/v1/books/${encodeURIComponent(book.id)}/source${query}`, controller.signal)
          .then(data => { if (!controller.signal.aborted) setSource({ key, text: data.text }); })
          .catch(failure => { if (!controller.signal.aborted) setError(I18n.describe(failure)); })
          .finally(() => { if (!controller.signal.aborted) setLoading(false); });
      }
      return () => controller.abort();
    }, [account.client, reader, key, accessKnown, requiresLogin]);
    return e('section', { className: 'source-reader', 'aria-label': t('Selected text') },
      e('div', { className: 'source-reader-heading' }, e('h2', null, section ? window.TymChapters.sectionLabel(section, book.sections.indexOf(section), locale, { useHeadingLanguage: false }) : t('Full Text'))),
      !accessKnown ? e('p', { role: 'status' }, t(account.error || 'Loading text access...'))
        : requiresLogin && !reader ? e('button', { type: 'button', className: 'secondary', onClick: onSignIn }, t('Sign in to read text'))
        : loading ? e('p', { role: 'status' }, t('Loading text...'))
          : error ? e('p', { role: 'alert', className: 'message error' }, t(error))
            : source?.key === key ? e('pre', { className: 'source-prose', lang: language, tabIndex: 0 }, source.text) : null);
  }

  root.TymAccounts = { useAccount, AccountDialog, SourceReader };
})(window);
