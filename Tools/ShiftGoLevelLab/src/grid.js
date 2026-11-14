/* 棋盘渲染器：DOM 格子。
 *
 * 为什么从 canvas 换成 DOM：格子里要写字（元件缩写、箭头、角标、坐标、位），
 * canvas 得自己算字号和基线，格子一小就糊；交给浏览器排版则天然清晰，
 * 还白拿原生 title 悬浮提示与 CSS 状态类。棋盘上限 40×40，节点量扛得住。
 *
 * 坐标约定：y=0 在最下面，与 wallRows 的行序、游戏内的网格一致。
 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  var D = root.BitFlowDir;

  var KIND_COLORS = {
    0: '#5b6673', // Wall
    1: '#b48ead', // Emitter
    2: '#5aa9e6', // Receiver
    3: '#e0a458', // Splitter
    4: '#7ee787', // NotGate
    5: '#d98cb3', // Filter
    6: '#8fbcbb'  // DelayGate
  };

  var KIND_SHORT = { 0: '#', 1: 'E', 2: 'R', 3: 'L', 4: 'N', 5: 'F', 6: 'D' };

  var KIND_LABELS = {
    0: '墙 Wall',
    1: '发射器 Emitter',
    2: '接收器 Receiver',
    3: '分流器 Splitter',
    4: '非门 NotGate',
    5: '过滤器 Filter',
    6: '延迟器 DelayGate'
  };

  var DIR_NAMES = ['Up', 'Right', 'Down', 'Left'];

  function key(x, y) { return x + ',' + y; }

  function span(className) {
    var node = document.createElement('span');
    node.className = className;
    return node;
  }

  function directionList(element) {
    if (!element) return [];
    if (element.kind === 1) return [element.facing];
    if (element.kind === 2) return element.acceptAnyFace ? [] : [element.feedFace];
    if (element.kind === 3) {
      var list = [element.oneArm];
      if (element.zeroArm !== undefined && element.zeroArm !== element.oneArm) list.push(element.zeroArm);
      return list;
    }
    if (element.kind === 4 || element.kind === 5) return [element.outputDir];
    return [];
  }

  function badgeText(element) {
    if (!element) return '';
    if (element.kind === 1 && isFinite(element.emitCount)) return '×' + element.emitCount;
    if (element.kind === 2 && isFinite(element.targetCount)) return '×' + element.targetCount;
    if (element.kind === 6 && isFinite(element.delayTicks)) return 'D' + element.delayTicks;
    return '';
  }

  /* 一格的全部信息：给 title 用，也给舞台右上角的坐标条用。 */
  function describeCell(document_, catalog, x, y) {
    var doc = document_ || {};
    var list = Array.isArray(doc.elements) ? doc.elements : [];
    var raw = null;
    for (var i = 0; i < list.length; i++) {
      if ((list[i].x | 0) === x && (list[i].y | 0) === y) { raw = list[i]; break; }
    }
    var element = raw ? Doc.resolveCell(raw, catalog) : null;
    var rows = Array.isArray(doc.wallRows) ? doc.wallRows : [];
    var isWall = (rows[y] || '').charAt(x) === '#';

    var parts = ['(' + x + ', ' + y + ')'];
    if (element) {
      parts.push(KIND_LABELS[element.kind] || ('kind ' + element.kind));
      var dirs = directionList(element);
      if (dirs.length === 1) parts.push('朝向 ' + D.mark(dirs[0]));
      else if (dirs.length > 1) parts.push('出口 ' + dirs.map(function (dir) { return D.mark(dir); }).join(' '));
      if (element.kind === 2) parts.push(element.acceptAnyFace ? '四面可收' : '只认进料面');
      if (element.kind === 1 && raw) parts.push('序列 ' + (raw.sequence || '') );
      if (element.kind === 2 && isFinite(element.targetCount)) parts.push('目标 ×' + element.targetCount);
      if (element.kind === 6 && isFinite(element.delayTicks)) parts.push('停 ' + element.delayTicks + ' 拍');
      if (raw && raw.type) parts.push('type ' + raw.type);
      if (element.playerEditable) parts.push('玩家可搬');
    } else if (isWall) {
      parts.push('墙');
    } else {
      parts.push('空');
    }
    return parts.join(' · ');
  }

  /* 从任意事件反查格子（事件可能落在 span 上）。 */
  function cellFromEvent(event) {
    var node = event && event.target ? event.target : null;
    while (node && node.classList) {
      if (node.classList.contains('cell')) {
        var x = parseInt(node.getAttribute('data-x'), 10);
        var y = parseInt(node.getAttribute('data-y'), 10);
        if (isFinite(x) && isFinite(y)) return { x: x, y: y };
        return null;
      }
      node = node.parentNode;
    }
    return null;
  }

  function Grid(container) {
    this.container = container;
    this.viewport = container.parentNode || container;
    this.inner = document.createElement('div');
    this.inner.className = 'grid-inner';
    container.appendChild(this.inner);

    this.document = null;
    this.catalog = null;
    this.frame = null;
    this.selection = null;
    this.hover = null;
    this.brush = null;
    this.tool = 'paint';

    this.width = 0;
    this.height = 0;
    this.builtKey = '';
    this.cells = [];
    this.cellSize = 32;

    /* 舞台高度会随「清单 / 检查器」内容变化，光靠 window.resize 不够。 */
    var self = this;
    this.observer = null;
    if (typeof ResizeObserver === 'function') {
      this.observer = new ResizeObserver(function () { self.draw(); });
      this.observer.observe(this.viewport);
    }
  }

  /* 部分更新：ui.js 会分几次塞 document / frame / hover。 */
  Grid.prototype.setState = function (state) {
    if (!state) return;
    if (state.document) this.document = state.document;
    if (state.catalog) this.catalog = state.catalog;
    if (state.frame !== undefined) this.frame = state.frame;
    if (state.selection !== undefined) this.selection = state.selection;
    if (state.hover !== undefined) this.hover = state.hover;
    if (state.brush !== undefined) this.brush = state.brush;
    if (state.tool !== undefined) this.tool = state.tool;
    this.draw();
  };

  Grid.prototype.resize = function () { this.draw(); };

  Grid.prototype.draw = function () {
    if (!this.document) return;
    this.build();
    this.layout();
    this.paint();
  };
  /* 量父容器（滚动盒）而不是 #grid 自身：内容变大会把 #grid 撑大，
   * 反过来再量它就会越算越大。 */
  Grid.prototype.layout = function () {
    var box = this.viewport;
    var width = Math.max(1, this.width);
    var height = Math.max(1, this.height);
    var rect = box.getBoundingClientRect ? box.getBoundingClientRect() : { width: 640, height: 480 };
    var gap = 3;
    var availWidth = Math.max(200, (rect.width || 640) - 24);
    var availHeight = Math.max(160, (rect.height || 480) - 24);

    var size = Math.floor(Math.min(
      (availWidth - gap * (width - 1)) / width,
      (availHeight - gap * (height - 1)) / height
    ));
    if (!isFinite(size)) size = 32;
    size = Math.max(26, Math.min(96, size));

    this.cellSize = size;
    this.inner.style.width = (size * width + gap * (width - 1)) + 'px';
    this.inner.style.height = (size * height + gap * (height - 1)) + 'px';
    this.inner.style.gridTemplateColumns = 'repeat(' + width + ', ' + size + 'px)';
    this.inner.style.gridTemplateRows = 'repeat(' + height + ', ' + size + 'px)';
    this.inner.style.setProperty('--cell', size + 'px');
    /* 格子够大才显示格内坐标，否则太挤。 */
    this.inner.classList.toggle('with-coords', size >= 46);
  };

  /* 尺寸变了才重建节点；其余情况复用，逐 tick 重画才不卡。 */
  Grid.prototype.build = function () {
    var doc = this.document;
    var width = Math.max(1, doc.width | 0);
    var height = Math.max(1, doc.height | 0);
    var signature = width + 'x' + height;

    this.width = width;
    this.height = height;
    if (signature === this.builtKey) return false;

    this.inner.innerHTML = '';
    this.cells = [];

    for (var row = 0; row < height; row++) {
      for (var col = 0; col < width; col++) {
        var node = document.createElement('div');
        node.className = 'cell';
        node.setAttribute('data-x', String(col));
        node.setAttribute('data-y', String(height - 1 - row));

        node.appendChild(span('coord'));
        node.appendChild(span('glyph'));

        var arrows = [];
        for (var d = 0; d < 4; d++) {
          var arrow = span('arrow ' + DIR_NAMES[d].toLowerCase());
          node.appendChild(arrow);
          arrows.push(arrow);
        }
        node.appendChild(span('badge'));
        node.appendChild(span('bit'));

        node._coord = node.children[0];
        node._glyph = node.children[1];
        node._arrows = arrows;
        node._badge = node.children[6];
        node._bit = node.children[7];

        this.inner.appendChild(node);
        this.cells.push(node);
      }
    }

    this.builtKey = signature;
    return true;
  };
  Grid.prototype.paint = function () {
    var doc = this.document;
    var width = this.width;
    var height = this.height;
    var rows = Array.isArray(doc.wallRows) ? doc.wallRows : [];
    var elements = Array.isArray(doc.elements) ? doc.elements : [];
    var catalog = this.catalog;

    var byCell = {};
    var byId = {};
    for (var i = 0; i < elements.length; i++) {
      var element = Doc.resolveCell(elements[i], catalog);
      if (!element) continue;
      byCell[key(element.x, element.y)] = { element: element, cell: elements[i] };
      if (element.id !== undefined && element.id !== null) byId[element.id] = element;
    }

    var bits = {};
    var frame = this.frame;
    if (frame && frame.bits) {
      for (var b = 0; b < frame.bits.length; b++) {
        bits[key(frame.bits[b].x, frame.bits[b].y)] = frame.bits[b].value;
      }
    }

    var hit = {};
    var miss = {};
    var destroyed = {};
    var absorbed = {};
    var report = frame && frame.report ? frame.report : null;
    if (report) {
      for (var g = 0; g < report.destroyed.length; g++) {
        destroyed[key(report.destroyed[g].x, report.destroyed[g].y)] = true;
      }
      for (var h = 0; h < report.delayAbsorbed.length; h++) {
        absorbed[key(report.delayAbsorbed[h].x, report.delayAbsorbed[h].y)] = true;
      }
      for (var de = 0; de < report.deliveries.length; de++) {
        var target = byId[report.deliveries[de].receiverId];
        if (!target) continue;
        if (report.deliveries[de].matched) hit[key(target.x, target.y)] = true;
        else miss[key(target.x, target.y)] = true;
      }
    }

    var selection = this.selection;
    var hover = this.hover;
    var brush = this.brush;
    var ghost = this.tool === 'paint' && brush && brush.kind !== Doc.KIND.Wall;

    this.inner.classList.toggle('tool-select', this.tool === 'select');
    this.inner.classList.toggle('tool-paint', this.tool !== 'select');

    for (var row = 0; row < height; row++) {
      var gridY = height - 1 - row;
      var line = rows[gridY] || '';
      for (var col = 0; col < width; col++) {
        var node = this.cells[row * width + col];
        var cellKey = key(col, gridY);
        var entry = byCell[cellKey] || null;
        var element = entry ? entry.element : null;
        var isWall = line.charAt(col) === '#';
        var isHovered = !!(hover && hover.x === col && hover.y === gridY);

        var classes = ['cell'];
        if (element) classes.push('kind-' + element.kind);
        else if (isWall) classes.push('wall');
        else classes.push('empty');
        if (element && element.playerEditable) classes.push('pe');
        if (selection && selection.x === col && selection.y === gridY) classes.push('selected');
        if (isHovered) classes.push('hover');
        if (destroyed[cellKey]) classes.push('fx-destroy');
        if (hit[cellKey]) classes.push('fx-hit');
        if (miss[cellKey]) classes.push('fx-miss');
        if (absorbed[cellKey]) classes.push('fx-absorb');
        node.className = classes.join(' ');

        node.title = describeCell(doc, catalog, col, gridY);
        node._coord.textContent = col + ',' + gridY;

        node._glyph.className = 'glyph';
        if (element) {
          node._glyph.textContent = KIND_SHORT[element.kind] || '?';
        } else if (isWall) {
          node._glyph.textContent = '#';
        } else if (ghost && isHovered) {
          node._glyph.textContent = KIND_SHORT[brush.kind] || '?';
          node._glyph.className = 'glyph ghost-glyph';
        } else {
          node._glyph.textContent = '';
        }

        var dirs = directionList(element);
        for (var d = 0; d < 4; d++) {
          node._arrows[d].textContent = dirs.indexOf(d) >= 0 ? D.mark(d) : '';
        }

        node._badge.textContent = badgeText(element);

        var value = bits[cellKey];
        node._bit.textContent = (value === 0 || value === 1) ? String(value) : '';
        node._bit.className = 'bit' + (value === 1 ? ' b1' : (value === 0 ? ' b0' : ''));
      }
    }
  };

  var api = {
    Grid: Grid,
    KIND_COLORS: KIND_COLORS,
    KIND_SHORT: KIND_SHORT,
    KIND_LABELS: KIND_LABELS,
    describeCell: describeCell,
    cellFromEvent: cellFromEvent
  };

  root.BitFlowGrid = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
