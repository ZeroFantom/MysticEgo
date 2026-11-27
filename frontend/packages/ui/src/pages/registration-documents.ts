type DocumentLink = {version: string; url: string};
export type RegistrationDocuments = {required: true; terms: DocumentLink; privacy: DocumentLink; license: DocumentLink; permissionPolicyVersion:string; scopes:string[]};

export async function readRegistrationDocuments(authUrl: string): Promise<RegistrationDocuments> {
  const response = await fetch(authUrl.replace(/\/$/, '') + '/api/auth/audit/registration-documents', {
    cache: 'no-store',
    signal: AbortSignal.timeout(12000),
  });

  if (!response.ok) throw Error('Документы регистрации недоступны.');

  const data = await response.json() as RegistrationDocuments;
  if (data.required) {
    for (const item of [data.terms, data.privacy, data.license]) {
      const url = new URL(item.url);
      if (!item.version || url.protocol !== 'https:' || url.username || url.password)
        throw Error('Сервер вернул неверную ссылку на документы.');
    }
    if(!data.permissionPolicyVersion||!Array.isArray(data.scopes)||!data.scopes.every(scope=>typeof scope==='string'))throw Error('Invalid permission policy');
  } else {
    throw Error('Сервер вернул неверные данные регистрации.');
  }

  return data;
}
