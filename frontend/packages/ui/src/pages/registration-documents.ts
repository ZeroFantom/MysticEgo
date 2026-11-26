type DocumentLink = {version: string; url: string};
export type RegistrationDocuments = {required: false} | {required: true; terms: DocumentLink; privacy: DocumentLink};

export async function readRegistrationDocuments(authUrl: string): Promise<RegistrationDocuments> {
  const response = await fetch(authUrl.replace(/\/$/, '') + '/api/auth/audit/registration-documents', {
    cache: 'no-store',
    signal: AbortSignal.timeout(12000),
  });

  // Older nodes do not expose registration policy metadata.
  if (response.status === 404) return {required: false};
  if (!response.ok) throw Error('Документы регистрации недоступны.');

  const data = await response.json() as RegistrationDocuments;
  if (data.required) {
    for (const item of [data.terms, data.privacy]) {
      const url = new URL(item.url);
      if (!item.version || url.protocol !== 'https:' || url.username || url.password)
        throw Error('Сервер вернул неверную ссылку на документы.');
    }
  } else if (data.required !== false) {
    throw Error('Сервер вернул неверные данные регистрации.');
  }

  return data;
}
