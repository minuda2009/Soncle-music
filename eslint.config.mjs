export default [
  {
    files: ['src/**/*.mjs', 'src/**/*.cjs', 'renderer/**/*.js', 'test/**/*.mjs', 'tools/**/*.mjs', 'mobile/src/**/*.js', 'mobile/*.mjs', 'mobile/tools/**/*.mjs'],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'module',
      globals: {
        window: 'readonly', document: 'readonly', navigator: 'readonly', console: 'readonly',
        process: 'readonly', Buffer: 'readonly', require: 'readonly', module: 'readonly',
        Audio: 'readonly', AudioContext: 'readonly', Image: 'readonly', MediaMetadata: 'readonly',
        IntersectionObserver: 'readonly', ResizeObserver: 'readonly', matchMedia: 'readonly',
        innerWidth: 'readonly', innerHeight: 'readonly', devicePixelRatio: 'readonly',
        setTimeout: 'readonly', clearTimeout: 'readonly', setInterval: 'readonly', clearInterval: 'readonly',
        requestAnimationFrame: 'readonly', cancelAnimationFrame: 'readonly', fetch: 'readonly',
        URL: 'readonly', URLSearchParams: 'readonly', Response: 'readonly', Request: 'readonly',
        Headers: 'readonly', AbortController: 'readonly', CustomEvent: 'readonly', Event: 'readonly',
        MouseEvent: 'readonly', crypto: 'readonly', structuredClone: 'readonly', Float32Array: 'readonly',
        getComputedStyle: 'readonly', Element: 'readonly', EventTarget: 'readonly', Intl: 'readonly',
        Worker: 'readonly', self: 'readonly', OfflineAudioContext: 'readonly', performance: 'readonly', PointerEvent: 'readonly', WheelEvent: 'readonly', ReadableStream: 'readonly', AudioWorkletNode: 'readonly', MediaSource: 'readonly', KeyboardEvent: 'readonly', location: 'readonly', Uint8Array: 'readonly', atob: 'readonly', btoa: 'readonly', TextEncoder: 'readonly', TextDecoder: 'readonly'
      }
    },
    rules: {
      'no-undef': 'error',
      'no-unused-vars': ['warn', { args: 'none', varsIgnorePattern: '^_' }],
      'no-const-assign': 'error',
      'no-dupe-keys': 'error',
      'no-dupe-args': 'error',
      'no-duplicate-case': 'error',
      'no-func-assign': 'error',
      'no-cond-assign': 'error',
      'no-unreachable': 'error',
      'no-unsafe-negation': 'error',
      'valid-typeof': 'error',
      'no-self-assign': 'error',
      'no-sparse-arrays': 'error'
    }
  }
];
