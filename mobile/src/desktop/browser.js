// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/browser for the Windows app: links open in the default browser, through the host.
import { post } from './host.js';

export const Browser = {
  open: async ({ url }) => post({ type: 'open', url: String(url) }),
};
