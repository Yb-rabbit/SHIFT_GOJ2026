/* 内置文档：单文件交付不能读 docs/*.md，帮助面板的内容就写在这里。
 * 只写"作者改关卡时真正会用到的"，不复述 C# 注释。 */
(function (root) {
  'use strict';

  var SECTIONS = [
    {
      title: '5 分钟上手',
      html: [
        '<p>左边是棋盘，右边是检查器。点画笔再点格子就能放件；右键擦除；按住 Shift 点件是旋转。</p>',
        '<ol>',
        '<li>选一件画笔（或按 1-7），在格子上落件。</li>',
        '<li>选中格子后在右边改参数：朝向、序列、数量、进料面、延迟 tick。</li>',
        '<li>点 <b>自检</b>：这里跑的就是游戏里那份模拟器，结论措辞也和游戏一致。</li>',
        '<li>时间轴可以逐 tick 前进/后退/播放，确认第一次销毁、第一次收货发生在第几拍。</li>',
        '<li>导出 JSON，放回 <code>Resources/BitFlow/Levels/</code> 或直接在游戏里导入。</li>',
        '</ol>',
        '<p>所有数据都自动存在浏览器本地（localStorage）。换浏览器/清缓存就没了，重要的关一定导出。</p>'
      ].join('')
    },
    {
      title: '关卡 JSON（schema 2）',
      html: [
        '<p>字段只增不改：旧文件缺字段就用默认值。坐标 y 向上，<code>wallRows[0]</code> 是最下面一行（y=0）。</p>',
        '<table class="doc-table"><thead><tr><th>字段</th><th>说明</th></tr></thead><tbody>',
        '<tr><td>schema</td><td>当前 2</td></tr>',
        '<tr><td>displayName / width / height / tickInterval</td><td>关卡名、尺寸、每 tick 秒数（0.05~5）</td></tr>',
        '<tr><td>wallRows</td><td>墙字符画，一行一个 y，<code>#</code> 是墙；也可用旧写法 <code>wallMap</code>（\\n 分行）</td></tr>',
        '<tr><td>elements[]</td><td>元件格：<code>type</code>（精确类型）或 <code>kind</code>（宽松），<code>x/y</code>，<code>rot</code>，<code>dir/dir2</code>，<code>sequence</code>，<code>count</code>，<code>face</code>，<code>pass</code>，<code>delay</code>，<code>playerEditable</code></td></tr>',
        '<tr><td>supplies[]</td><td>手牌：<code>type</code>/<code>kind</code> + <code>count</code>（-1 = 1 张，0 = 跳过并报警）</td></tr>',
        '<tr><td>allowBuild</td><td>允许玩家进关后自己搬件；有手牌时自动视为 true</td></tr>',
        '</tbody></table>',
        '<p><b>type 模式</b>：朝向由类型快照 + <code>rot</code> 决定，且 <code>pass</code> 被忽略（游戏里就是这么读的）。<br>',
        '<b>kind 模式</b>：<code>dir</code>/<code>dir2</code> 是绝对朝向，<code>rot</code> 不参与。<br>',
        '<code>type</code> 在本工具里找不到时，会退回 <code>kind</code> 的默认值并记一条问题 —— 和游戏侧一致。</p>'
      ].join('')
    },
    {
      title: '元件规则（模拟器语义）',
      html: [
        '<table class="doc-table"><thead><tr><th>件</th><th>行为</th></tr></thead><tbody>',
        '<tr><td>发射器 E</td><td>按朝向每 tick 吐一位；前方被占/是墙/是发射器机身就顺延。序列按"末字符先弹"组成循环单元，再循环填满 <code>count</code> 位（count=6、序列 1001 → 1,0,0,1,1,0）。</td></tr>',
        '<tr><td>接收器 R</td><td>默认四面可收；<code>face=in</code> 时只认进料面，其余面撞上即销毁。按 <code>sequence</code> 循环逐位比对，收满 <code>count</code> 且全对才算成功，超收也算失败。</td></tr>',
        '<tr><td>分流器 L</td><td>值 1 走 OneArm、值 0 走 ZeroArm；从任一出口面逆行撞入 → 销毁。</td></tr>',
        '<tr><td>非门 N</td><td>值取反后从输出面射出；逆行 → 销毁。</td></tr>',
        '<tr><td>过滤器 F</td><td>只放行指定值，其余销毁；逆行 → 销毁。</td></tr>',
        '<tr><td>延迟器 D</td><td>进队即离场（不占格），比直通多停 N 拍后从进入方向原样穿出。队列上限 8，满了再进 → 销毁。四面等价，旋转没有意义。</td></tr>',
        '<tr><td>墙</td><td>撞上即销毁；发射器正前方是墙则永久顺延。</td></tr>',
        '</tbody></table>',
        '<p>每 tick 固定四步：<b>延迟器放行 → 发射 → 前进（含链式阻塞/抢格/对穿）→ 元件裁决</b>。顺序换了结果就会变，所以时间轴逐 tick 回放和游戏逐帧一致。</p>',
        '<p>结束条件：所有发射器吐完、场上无位、延迟器无存货；或到达 5000 tick 安全上限（自检会额外用 max(128, W×H×8) 兜底，两者取大）。</p>'
      ].join('')
    },
    {
      title: '自检在查什么',
      html: [
        '<p>不是"能跑就存"，只查那些<b>无论玩家怎么动都救不回来</b>的错：</p>',
        '<ul>',
        '<li>没有接收器 / 没有发射器（Blocking）</li>',
        '<li>没有手牌，但预设布局跑不通（Blocking）</li>',
        '<li>手牌里发了发射器或接收器（Blocking —— 那是题面，不是牌）</li>',
        '<li>空位格数 &lt; 总牌数（Blocking —— 一定有几张放不下）</li>',
        '<li>有手牌，但预设布局自己就能通关（Advisory —— 手牌是摆设，能存但该想想）</li>',
        '</ul>',
        '<p>报告措辞与游戏内自检一致：<code>✅ 通关</code> / <code>📝 预设布局未通关（有手牌的关，这是预期）</code> / <code>❌ 未通过</code>；实收超过 24 位会截断。</p>'
      ].join('')
    },
    {
      title: '手牌试玩（Solve）',
      html: [
        '<p>手牌关光看自检不够：得亲手按<b>玩家规则</b>摆一遍，确认这套牌真能解。点舞台栏的 <b>🧩 试玩</b>。</p>',
        '<ul>',
        '<li>棋盘换上手牌的临时副本：左侧 <b>手牌</b> 栏列出每摞牌与余量，点一张牌选中，再点空格落件。</li>',
        '<li><b>锁定件</b> = 进场时 <code>playerEditable=false</code> 的那些格：搬不动、也压不掉；指针落在上面会直接拒绝。</li>',
        '<li>点自己放的件 → 拾回原摞（两摞同类型也不会串味）；右键 / Shift+左键 → 旋转（墙、延迟器不转）。</li>',
        '<li><b>BUFFER</b> 这类工具卡不上棋盘：选中它再点锁定件，就把那枚预设件收回手牌。</li>',
        '<li>试玩有独立撤销栈（40 步），Ctrl+Z 撤销的是试玩，不碰作者的编辑历史。</li>',
        '<li>点 <b>自检</b>：跑通后「采用解」才可点 —— 它把这些件写成预设件（<code>playerEditable=false</code>），手牌原样保留。</li>',
        '<li>点「退出」直接丢弃试玩布局，回到试玩前的预设；试玩期间不写回、也不覆盖本地自动保存。</li>',
        '</ul>',
        '<p>规则本身写在 <code>src/hand.js</code>，有 <code>tests/hand.js</code> 单测覆盖；界面只负责把点击翻成"人话"提示。</p>'
      ].join('')
    },
    {
      title: '键盘与鼠标',
      html: [
        '<table class="doc-table"><thead><tr><th>操作</th><th>效果</th></tr></thead><tbody>',
        '<tr><td>左键</td><td>用当前画笔落件；<b>点在已有元件的格子上只选中，不会覆写</b></td></tr>',
        '<tr><td>右键</td><td>擦除（元件与墙一起清；空格上右键只选中）</td></tr>',
        '<tr><td>选择工具 V / 0 / Esc</td><td>进入纯选择状态：左键只选，不落件</td></tr>',
        '<tr><td>Shift + 左键 / R</td><td>旋转选中件（延迟器跳过）</td></tr>',
        '<tr><td>1 ~ 7</td><td>切换画笔：墙 / 发射器 / 接收器 / 分流器 / 非门 / 过滤器 / 延迟器</td></tr>',
        '<tr><td>Ctrl + Z / Ctrl + Y / Ctrl + Shift + Z</td><td>撤销 / 重做（最多 60 步；导入、载入样例会清空历史）</td></tr>',
        '<tr><td>Delete / Backspace</td><td>删除选中件</td></tr>',
        '<tr><td>← →</td><td>时间轴单步</td></tr>',
        '<tr><td>空格</td><td>播放 / 暂停</td></tr>',
        '<tr><td>Ctrl + S</td><td>导出 JSON（不触发浏览器保存网页）</td></tr>',
        '</tbody></table>'
      ].join('')
    },
    {
      title: '为什么能相信它和游戏一致',
      html: [
        '<p>浏览器里的这份引擎是 <code>Assets/Scripts/Gameplay/BitFlow/Runtime/Core/*.cs</code> 的逐行移植，',
        '并且用真代码做过对拍：</p>',
        '<ul>',
        '<li><code>tests/parity-csharp/</code> 用 <code>Compile Include</code> 把<b>游戏同一份 Core 源文件</b>编进一个 dotnet 控制台程序，',
        '读同一批 fixture，导出 expected-csharp.json。</li>',
        '<li><code>node tests/parity.js --check</code> 把 JS 结果与它逐字段比对：',
        '元件定义（含 Id 分配顺序）、构建问题文案、tick 数、结算明细、自检意见、报告全文。</li>',
        '</ul>',
        '<p>当前状态见页脚：对拍 fixture 数、自检通过项数、构建时间。</p>'
      ].join('')
    }
  ];

  var api = { SECTIONS: SECTIONS };
  root.BitFlowDocs = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
