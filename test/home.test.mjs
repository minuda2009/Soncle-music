import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as yt from '../src/yt.mjs';

test('homeMore ends quietly when YouTube returns an empty continuation', async () => {
  const feed = {
    filters: [], sections: [], has_continuation: true,
    getContinuation: async () => { throw new Error('Continuation did not have any content.'); }
  };
  yt.__setClient({ music: { getHomeFeed: async () => feed } });
  await yt.home();
  assert.deepEqual(await yt.homeMore(), { sections: [], hasMore: false });
  // and stays ended afterwards
  assert.deepEqual(await yt.homeMore(), { sections: [], hasMore: false });
});
