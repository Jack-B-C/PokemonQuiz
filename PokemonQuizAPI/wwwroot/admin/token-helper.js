// Tiny helper to prompt for Bearer token and use it for API calls on the admin SPA
(function(){
  function getToken(){
    return localStorage.getItem('admin_token') || '';
  }
  function setToken(t){
    localStorage.setItem('admin_token', t || '');
  }

  function showUnauthorizedOverlay(){
    try{
      var el = document.getElementById('unauthOverlay');
      if(!el){
        // create a simple overlay if not present
        el = document.createElement('div');
        el.id = 'unauthOverlay';
        el.style.position = 'fixed';
        el.style.inset = '0';
        el.style.background = 'rgba(0,0,0,0.6)';
        el.style.display = 'flex';
        el.style.alignItems = 'center';
        el.style.justifyContent = 'center';
        el.style.zIndex = '9999';
        el.innerHTML = '<div style="background:#fff;padding:22px;border-radius:10px;max-width:760px;width:92%;box-shadow:0 8px 30px rgba(0,0,0,0.3);font-family:Arial,Helvetica,sans-serif"><h2 style="margin:0 0 8px">Access denied</h2><p style="margin:0 0 12px;color:#374151">You are not authorized to access the admin API. Please provide a valid administrator token below to continue.</p><div style="display:flex;gap:8px"><input id="unauthTokenInput" placeholder="Paste admin token here" style="flex:1;padding:8px;border:1px solid #e5e7eb;border-radius:6px"/><button id="unauthSaveBtn" style="background:#0b61c6;color:#fff;border:none;padding:8px 12px;border-radius:6px;cursor:pointer">Save token</button><button id="unauthCloseBtn" style="background:transparent;color:#374151;border:1px solid #e5e7eb;padding:8px 12px;border-radius:6px;cursor:pointer">Close</button></div></div>';
        document.body.appendChild(el);
        document.getElementById('unauthSaveBtn').addEventListener('click', function(){
          var v = (document.getElementById('unauthTokenInput')||{}).value || '';
          if(v){ setToken(v); hideUnauthorizedOverlay(); location.reload(); }
        });
        document.getElementById('unauthCloseBtn').addEventListener('click', function(){ hideUnauthorizedOverlay(); });
      } else {
        el.style.display = 'flex';
      }
    }catch(e){ /* ignore DOM issues */ }
  }

  function hideUnauthorizedOverlay(){
    try{
      var el = document.getElementById('unauthOverlay');
      if(el) el.style.display = 'none';
    }catch(e){}
  }

  // If a token is provided in the URL hash (e.g. /admin#token=...), capture and save it
  try {
    if (typeof location !== 'undefined' && location.hash) {
      const m = location.hash.match(/token=([^&]+)/);
      if (m && m[1]) {
        try {
          const decoded = decodeURIComponent(m[1]);
          setToken(decoded);
          // remove token from hash to avoid leaking it in history
          history.replaceState(null, '', location.pathname + location.search);
        } catch (e) { /* ignore */ }
      }
    }
  } catch (e) { /* ignore in non-browser contexts */ }

  window.adminAuth = {
    getToken, setToken,
    showUnauthorizedOverlay: showUnauthorizedOverlay,
    hideUnauthorizedOverlay: hideUnauthorizedOverlay,
    fetchWithToken: async function(url, opts){
      opts = opts || {};
      opts.headers = opts.headers || {};
      const t = getToken();
      if (t) opts.headers['Authorization'] = 'Bearer ' + t;
      try{
        const res = await fetch(url, opts);
        if(res.status === 401 || res.status === 403){
          // show friendly overlay to allow pasting token
          showUnauthorizedOverlay();
        }
        return res;
      }catch(err){
        // network errors should be surfaced to the caller
        throw err;
      }
    }
  };
})();
