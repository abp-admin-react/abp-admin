import { useRef, useState } from 'react';

export function useCredentialProbe<T>() {
  const version = useRef(0);
  const pending = useRef(false);
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<T>();

  function clear() {
    version.current += 1;
    setResult(undefined);
  }

  async function test(query: () => Promise<T>, unavailable: T) {
    if (pending.current) return;
    pending.current = true;
    const request = ++version.current;
    setTesting(true);
    setResult(undefined);
    try {
      const response = await query();
      if (version.current === request) setResult(response);
    } catch {
      if (version.current === request) setResult(unavailable);
    } finally {
      pending.current = false;
      setTesting(false);
    }
  }

  return { clear, result, test, testing };
}
