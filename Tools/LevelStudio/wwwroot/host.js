/* 编辑器前端与「宿主」之间唯一的接缝。
 *
 * app.js 只认 HOST.xxx，不认识 /api/* —— 这样同一份 app.js 可以被两种宿主驱动：
 *
 *   wwwroot/host.js       连本地 ASP.NET 服务（dotnet run / ShiftGoLevelStudio.bat）
 *   web-src/host-static.js 纯浏览器版（file:// 双击，零依赖）
 *
 * 加新能力时先想清楚「纯前端能不能自己做」：能就写进 host-static.js，
 * 不能（比如跑 Core 里的 BitFlowSimulation）才在这里加一条 /api 路由。
 *
 * 这个文件必须加载在 app.js 之前。用 window.HOST 而不是 const —— 两个 <script>
 * 之间的顶层 const 虽然共享全局词法环境，但显式挂 window 更不容易踩到顺序问题。
 */
(function () {
  async function getJson(path) {
    const response = await fetch(path);
    if (!response.ok) throw new Error('HTTP ' + response.status + ' ' + path);
    return response.json();
  }

  async function postJson(path, body) {
    const response = await fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!response.ok) throw new Error('HTTP ' + response.status + ' ' + path);
    return response.json();
  }

  window.HOST = {
    mode: 'http',

    /* 文件面板里「往哪儿存/从哪儿读」的默认值。静态版返回 'local'。 */
    defaultWhere() {
      return 'incoming';
    },

    /* 类型目录：Unity 侧 LevelTypeCatalogTool 导出的快照。 */
    catalog() {
      return getJson('/api/catalog');
    },

    /* 工程根、玩家存档目录、有没有类型快照 —— 只用来铺「文件」面板下面那行灰字。 */
    state() {
      return getJson('/api/state');
    },

    /* 目录列表。返回 { files:[{name,file,displayName,size,modified}], missing } */
    listLevels(where) {
      return getJson('/api/levels?where=' + encodeURIComponent(where));
    },

    /* 读一份关卡。返回 LevelDocument 原样对象；读不到返回 { error }。 */
    readLevel(where, name) {
      return getJson('/api/level?where=' + encodeURIComponent(where)
        + '&name=' + encodeURIComponent(name));
    },

    /* 存盘。返回 { ok, name, file, existed, renamedFrom, problems, saveProblems }。 */
    save(where, oldName, json) {
      return postJson('/api/save', { where: where, oldName: oldName, json: json });
    },

    /* 真引擎自测。body 就是 serializeDoc() 的结果。 */
    check(document) {
      return postJson('/api/check', document);
    },

    /* 在资源管理器里打开当前目录。 */
    openFolder(where) {
      return postJson('/api/open-folder', { where: where });
    },
  };
})();
