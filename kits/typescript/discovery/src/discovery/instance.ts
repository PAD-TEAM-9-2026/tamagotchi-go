import { readFileSync } from 'node:fs';

import { v7 as uuidv7 } from 'uuid';

function packageVersion(): string {
  const contents = readFileSync(new URL('../../package.json', import.meta.url), 'utf8');
  return (JSON.parse(contents) as { version: string }).version;
}

export const instance = {
  id: uuidv7(),
  version: packageVersion(),
  startedAt: Date.now(),
};
