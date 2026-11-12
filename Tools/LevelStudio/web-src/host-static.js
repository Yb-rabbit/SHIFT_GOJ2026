/* 纯静态版的 HOST —— 不连任何服务，一切都在浏览器里。
 *
 * 和 wwwroot/host.js 暴露完全相同的接口，所以 wwwroot/app.js 一行都不用改
 * （构建脚本 build-web.ps1 把 app.js 原样内联进来）。
 *
 * 和桌面板的真实差异只有三处，都是有意的：
 *   1. 没有磁盘目录：关卡存在 localStorage 里，"保存"= 存进浏览器
 *   2. 没有类型目录文件：类型快照随页面一起打包（window.__CATALOG__）
 *   3. 没有引擎：BitFlowSimulation 是 C#，纯浏览器跑不了 —— 自测按钮如实说"没跑"
 *
 * 第 3 条是这套方案唯一真正的代价。等 engine.js（Core 的 JS 移植）接上，
 * 把 HOST.check 换成 engine.check 就行，其余不动。
 */
(function () {
  // 关卡存在这里：{ [baseName]: { json, displayName, savedAt, size } }
  var STORE_KEY = 'shiftgo.studio.levels';

  function readStore() {
    try {
      var raw = window.localStorage.getItem(STORE_KEY);
      if (!raw) return {};
      var parsed = JSON.parse(raw);
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (error) {
      return {};
    }
  }

  function writeStore(store) {
    window.localStorage.setItem(STORE_KEY, JSON.stringify(store));
  }

  function sizeOf(text) {
    try {
      return new Blob([text]).size;
    } catch (error) {
      return text.length;
    }
  }

  /* 2026-11-13T08:04:05.000Z → 11-13 08:04（本地时区）。
     只为和桌面板那列的「修改时间」长得一样，不参与任何判定。 */
  function stampOf(iso) {
    var date = iso ? new Date(iso) : new Date();
    if (isNaN(date.getTime())) return '—';
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    return pad(date.getMonth() + 1) + '-' + pad(date.getDate())
      + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());
  }

  window.HOST = {
    mode: 'static',

    defaultWhere() {
      return 'local';
    },

    catalog() {
      var catalog = window.__CATALOG__;
      return Promise.resolve(catalog && catalog.types ? catalog : { schema: 2, types: [] });
    },

    state() {
      var types = (window.__CATALOG__ && window.__CATALOG__.types) || [];
      return Promise.resolve({
        mode: 'static',
        projectRoot: '—（独立版，不依赖 Unity 工程）',
        playerDir: '—（关卡存在浏览器本地，点「下载」取 .json）',
        hasCatalog: types.length > 0,
        catalogCount: types.length,
      });
    },

    listLevels() {
      var store = readStore();
      var files = Object.keys(store).map(function (name) {
        var entry = store[name] || {};
        return {
          name: name,
          file: name + '.json',
          displayName: entry.displayName || '',
          size: entry.size || sizeOf(String(entry.json || '')),
          modified: stampOf(entry.savedAt),
          savedAt: entry.savedAt || '',
        };
      });

      files.sort(function (a, b) {
        return String(b.savedAt).localeCompare(String(a.savedAt));
      });

      return Promise.resolve({ files: files, missing: false });
    },

    readLevel(where, name) {
      var entry = readStore()[name];
      if (!entry || !entry.json) {
        return Promise.resolve({ error: '本机存储里没有这份关卡：' + name });
      }

      try {
        return Promise.resolve(JSON.parse(entry.json));
      } catch (error) {
        return Promise.resolve({ error: '这份关卡存坏了：' + error.message });
      }
    },

    /* 保存 = 写进 localStorage。
     *
     * 不顺手触发下载：每次点保存都弹一次"已下载"，作者很快就学会无视它了。
     * 要拿文件就点「下载」——那是明确的一次动作，而且它和这里写的是同一份文本。
     *
     * 名字规则不在前端复制第二份：调 app.js 里的 sanitizeName / baseNameOf
     * （它们的镜像对象是 Core/LevelNaming.cs）。save 只在用户点按钮时被调用，
     * 那时 app.js 已经执行完，全局函数一定在。 */
    save(where, oldName, json) {
      var document;
      try {
        document = JSON.parse(json);
      } catch (error) {
        return Promise.resolve({ ok: false, error: 'JSON 拼坏了：' + error.message });
      }

      var display = sanitizeName(document.displayName);
      if (!display) {
        return Promise.resolve({
          ok: false,
          error: '关卡名不能为空。只能用 A-Z、0-9、- _ 和空格，最多 ' + LEVEL_NAMING.maxLength + ' 个字。',
        });
      }

      var name = baseNameOf(display);
      var store = readStore();

      // 名字撞车不许静默覆盖 —— 和 Api.cs 的 NameTaken 同一条规矩。
      // 例外：撞上的就是自己（同一个 baseName），那是覆盖。
      var existed = !!store[name];
      if (existed && oldName && oldName !== name) {
        return Promise.resolve({
          ok: false,
          error: '这个名字已经有人用了：' + name + '.json。换个名字再存。',
        });
      }

      var renamedFrom = null;
      if (oldName && oldName !== name && store[oldName]) {
        delete store[oldName];
        renamedFrom = oldName;
      }

      store[name] = {
        json: json,
        displayName: display,
        savedAt: new Date().toISOString(),
        size: sizeOf(json),
      };

      try {
        writeStore(store);
      } catch (error) {
        return Promise.resolve({
          ok: false,
          error: '浏览器本地存储写不进去了（可能满了）：' + error.message
            + '。点「下载」把这一关存成文件。',
        });
      }

      return Promise.resolve({
        ok: true,
        name: name,
        file: '本机存储 · ' + name + '.json',
        displayName: display,
        existed: existed,
        renamedFrom: renamedFrom,
        problems: [],
        saveProblems: [],
      });
    },

    /* 没接引擎时如实说"没跑"，不拿"结构看起来没问题"去冒充"能通关"。
     * app.js 见到 pending 就只展示 report，不会拼那句「存档前置检查：没有发现硬伤」。 */
    check() {
      return Promise.resolve({
        pending: true,
        problems: [],
        saveProblems: [],
        hasSupplies: false,
        pass: false,
        report: [
          '⚠ 独立版没有内置引擎，这一项没跑。',
          '',
          '「跑一遍真引擎」跑的是 Runtime/Core 里的 BitFlowSimulation（C#，762 行），',
          '纯浏览器版需要把它移植成 engine.js —— 那是下一步。',
          '',
          '现在能保证的：JSON 结构、关卡名规则、元件参数范围都在前端校验过；',
          '导出的文件就是游戏 IMPORT 直接吃的格式。',
        ].join('\n'),
      });
    },

    openFolder() {
      return Promise.resolve({
        ok: false,
        error: '独立版没有磁盘目录：「文件」里列的是存在浏览器本地的关卡，点「下载」就能拿到 .json。',
      });
    },
  };

  /* ------------------------------------------------------------------ 静态版专属的界面调整
   *
   * 磁盘目录这一套在 file:// 下没有意义，所以把「文件」面板改成只讲本机存储。
   * 放在这里而不是让 build-web.ps1 去改 HTML 文本：脚本保持纯 ASCII，
   * 中文只在源文件里出现一次，不会因为 PowerShell 的编码猜错而坏掉。
   *
   * 注意「打开目录」只能隐藏、不能删：app.js 的 bindUi() 会 document.getElementById
   * 拿它绑 onclick，元素没了就是一句 TypeError，整个编辑器起不来。
   */
  function rewriteFilePanel() {
    var where = document.getElementById('where');
    if (where) where.innerHTML = '<option value="local">本机存储（浏览器）</option>';

    var openFolder = document.getElementById('btnOpenFolder');
    if (openFolder) {
      openFolder.style.display = 'none';
      openFolder.setAttribute('aria-hidden', 'true');
    }

    document.title = 'SHIFT GO · BitFlow 关卡编辑器（独立版）';

    var brand = document.querySelector('.brand .dim');
    if (brand) brand.textContent = ' / BitFlow 关卡编辑器 · 独立版';
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', rewriteFilePanel);
  } else {
    rewriteFilePanel();
  }
})();

