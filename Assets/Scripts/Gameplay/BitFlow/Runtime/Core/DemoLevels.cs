namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 演示关卡的代码版定义。数据严格照抄设计文档"五、演示关卡（已验证可解）"。
    /// 它同时是：① 生成 Assets/Data/Levels/Level_01.asset 的数据源；② EditMode 测试的夹具。
    /// </summary>
    public static class DemoLevels
    {
        public const int Emitter1Id = 101;
        public const int Emitter2Id = 102;
        public const int Splitter1Id = 201;
        public const int NotGate1Id = 301;
        public const int Filter1Id = 401;
        public const int Receiver1Id = 501;
        public const int Receiver2Id = 502;
        public const int Receiver3Id = 503;
        public const int WallIdBase = 900;

        /// <summary>验收标准：这一关应在约 15 个 tick 内结算完毕。</summary>
        public const int ExpectedMaxTicks = 15;

        public static LevelDefinition CreateLevel01()
        {
            LevelDefinition level = new LevelDefinition
            {
                DisplayName = "LEVEL 01 - BIT FLOW",
                Width = 9,
                Height = 8,
                TickInterval = 0.5f,
            };

            // 发射器：序列从字符串末字符开始弹出组成循环单元，"1011" → 1,1,0,1；
            // 循环填充到 EmitCount=8 位 → 1,1,0,1,1,1,0,1
            level.Elements.Add(new EmitterDefinition
            {
                Id = Emitter1Id,
                Coord = new GridPoint(0, 5),
                Facing = Dir.Right,
                Sequence = "1011",
                EmitCount = 8,
            });

            // "100" → 0,0,1，共 3 位；随后被非门取反成 1,1,0
            level.Elements.Add(new EmitterDefinition
            {
                Id = Emitter2Id,
                Coord = new GridPoint(0, 1),
                Facing = Dir.Right,
                Sequence = "100",
                EmitCount = 3,
            });

            // 分流器：1 向上进 R1，0 向右进 R2
            level.Elements.Add(new SplitterDefinition
            {
                Id = Splitter1Id,
                Coord = new GridPoint(3, 5),
                OneArm = Dir.Up,
                ZeroArm = Dir.Right,
            });

            level.Elements.Add(new NotGateDefinition
            {
                Id = NotGate1Id,
                Coord = new GridPoint(2, 1),
                OutputDir = Dir.Right,
            });

            level.Elements.Add(new FilterDefinition
            {
                Id = Filter1Id,
                Coord = new GridPoint(5, 1),
                OutputDir = Dir.Right,
                PassValue = 1,
            });

            level.Elements.Add(new ReceiverDefinition
            {
                Id = Receiver1Id,
                Coord = new GridPoint(3, 7),
                FeedFace = Dir.Down,
                TargetSequence = "1",
                TargetCount = 6,
            });

            level.Elements.Add(new ReceiverDefinition
            {
                Id = Receiver2Id,
                Coord = new GridPoint(8, 5),
                FeedFace = Dir.Left,
                TargetSequence = "0",
                TargetCount = 2,
            });

            level.Elements.Add(new ReceiverDefinition
            {
                Id = Receiver3Id,
                Coord = new GridPoint(8, 1),
                FeedFace = Dir.Left,
                TargetSequence = "1",
                TargetCount = 2,
            });

            // 墙：四角 + 顶部右段 + 中部装饰。全部避开位流路径，不制造意外撞墙。
            AddWalls(level,
                new GridPoint(0, 0), new GridPoint(8, 0), new GridPoint(0, 7), new GridPoint(8, 7),
                new GridPoint(5, 7), new GridPoint(6, 7), new GridPoint(7, 7),
                new GridPoint(4, 3), new GridPoint(5, 3), new GridPoint(6, 3));

            return level;
        }

        static void AddWalls(LevelDefinition level, params GridPoint[] coords)
        {
            int id = WallIdBase;
            foreach (GridPoint coord in coords)
            {
                level.Elements.Add(new WallDefinition { Id = id++, Coord = coord });
            }
        }
    }
}
