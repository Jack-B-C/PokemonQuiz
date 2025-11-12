export async function fetchAuth(url: string, options: RequestInit = {}) {
  options = { ...options };
  options.headers = { ...(options.headers || {}) };
  const token = (global as any).userToken as string | undefined;
  if (token) {
    (options.headers as any)['Authorization'] = `Bearer ${token}`;
  }

  const res = await fetch(url, options);
  if (res.status === 401) {
    // clear client-side session and redirect to login when caller doesn't handle it
    try {
      (global as any).userToken = undefined;
      (global as any).userId = undefined;
      (global as any).username = undefined;
    } catch (e) { }
  }
  return res;
}
