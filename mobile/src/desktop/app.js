// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/app for the Windows app: there is no back button or background state to report.
export const App = {
  addListener: () => Promise.resolve({ remove() {} }),
  minimizeApp: async () => {},
};
