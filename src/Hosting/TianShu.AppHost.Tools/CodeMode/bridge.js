const __tianshuContentItems = Array.isArray(globalThis.__tianshuContentItems)
  ? globalThis.__tianshuContentItems
  : [];
const __tianshuRuntime = globalThis.__tianshuRuntime;

delete globalThis.__tianshuRuntime;

Object.defineProperty(globalThis, '__tianshuContentItems', {
  value: __tianshuContentItems,
  configurable: true,
  enumerable: false,
  writable: false,
});

(() => {
  if (!__tianshuRuntime || typeof __tianshuRuntime !== 'object') {
    throw new Error('code mode runtime is unavailable');
  }

  function defineGlobal(name, value) {
    Object.defineProperty(globalThis, name, {
      value,
      configurable: true,
      enumerable: true,
      writable: false,
    });
  }

  defineGlobal('ALL_TOOLS', __tianshuRuntime.ALL_TOOLS);
  defineGlobal('image', __tianshuRuntime.image);
  defineGlobal('load', __tianshuRuntime.load);
  defineGlobal('store', __tianshuRuntime.store);
  defineGlobal('text', __tianshuRuntime.text);
  defineGlobal('tools', __tianshuRuntime.tools);
  defineGlobal('yield_control', __tianshuRuntime.yield_control);

  defineGlobal(
    'console',
    Object.freeze({
      log() {},
      info() {},
      warn() {},
      error() {},
      debug() {},
    })
  );
})();

__CODE_MODE_USER_CODE_PLACEHOLDER__
