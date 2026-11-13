// BitFlow 浏览器 IO 桥（只有 WebGL 构建会编译进包）。
//
// 为什么需要它：浏览器沙箱里没有「文件夹」，Unity 的 System.IO 只有 /idbfs 这一块虚拟盘，
// 玩家的下载目录 / U 盘 / 聊天记录里的 json 一律看不见。唯一合规的入口是网页自己的控件：
//   进 = <input type="file">，出 = a[download]。
// 这里只做两件最小的事，判定与落盘逻辑全在 C#（BrowserJsonImport + LevelJsonLibrary）。
//
// 回传用 SendMessage：目标对象固定叫 BitFlowWebGLBridge（C# 侧 DontDestroyOnLoad）。
// 文件名与正文用 \u0001 拼成一个参数：SendMessage 只接受一个参数，
// 而 JSON 正文里不会出现这个控制字符。

mergeInto(LibraryManager.library, {
  BitFlow_PickJsonFile: function () {
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json,application/json';
    input.style.position = 'fixed';
    input.style.left = '-9999px';

    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) {
        SendMessage('BitFlowWebGLBridge', 'OnJsonPickCancelled', '');
        return;
      }

      var reader = new FileReader();
      reader.onload = function () {
        SendMessage('BitFlowWebGLBridge', 'OnJsonPicked',
          String(file.name) + '\u0001' + String(reader.result));
      };
      reader.onerror = function () {
        SendMessage('BitFlowWebGLBridge', 'OnJsonPickFailed', String(file.name));
      };
      reader.readAsText(file, 'utf-8');
    };

    // 玩家点「取消」：Chrome 113+ / Edge / Firefox 会发 cancel，老浏览器不发（那就当无事发生）。
    // 有它就够把面板从「正在挑文件」退回「已取消」，不用等超时。
    input.oncancel = function () {
      if (input.parentNode) input.parentNode.removeChild(input);
      SendMessage('BitFlowWebGLBridge', 'OnJsonPickCancelled', '');
    };

    // 有的浏览器要求 input 挂在文档里才会弹窗；弹完不急着删（读文件时还可能用到）
    document.body.appendChild(input);
    input.click();
    window.setTimeout(function () {
      if (input.parentNode) input.parentNode.removeChild(input);
    }, 60000);
  },

  BitFlow_DownloadText: function (fileNamePtr, textPtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var text = UTF8ToString(textPtr);

    var blob = new Blob([text], { type: 'application/json;charset=utf-8' });
    var url = URL.createObjectURL(blob);

    var link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.style.display = 'none';

    document.body.appendChild(link);
    link.click();

    window.setTimeout(function () {
      if (link.parentNode) link.parentNode.removeChild(link);
      URL.revokeObjectURL(url);
    }, 1000);
  }
});