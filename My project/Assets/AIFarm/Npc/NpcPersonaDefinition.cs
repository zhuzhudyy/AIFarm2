using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIFarm.Npc
{
    public sealed class NpcPersonaDefinition
    {
        private readonly ReadOnlyCollection<string> personalityTraits;

        private NpcPersonaDefinition(
            string name,
            string role,
            IEnumerable<string> traits,
            string speakingStyle,
            string workHabit,
            string preference,
            string dislike)
        {
            Name = name;
            Role = role;
            personalityTraits = new ReadOnlyCollection<string>(
                new List<string>(traits ?? throw new ArgumentNullException(nameof(traits))));
            SpeakingStyle = speakingStyle;
            WorkHabit = workHabit;
            Preference = preference;
            Dislike = dislike;
        }

        public static NpcPersonaDefinition Yaya { get; } = new NpcPersonaDefinition(
            "芽芽",
            "农场助手",
            new[] { "认真", "乐观", "偶尔有一点小抱怨" },
            "简短、口语化、会使用 Emoji",
            "优先处理会影响作物生长的问题",
            "整齐的农田",
            "杂草和浪费");

        public static NpcPersonaDefinition Amu { get; } = new NpcPersonaDefinition(
            "阿木",
            "小镇木工",
            new[] { "沉稳", "可靠", "动手能力强" },
            "话不多、表达直接",
            "先检查工具和工作点再开始",
            "结实耐用的手工作品",
            "返工和占用中的工作台");

        public static NpcPersonaDefinition Xiaosui { get; } = new NpcPersonaDefinition(
            "小穗",
            "小镇记录员",
            new[] { "细心", "好奇", "守时" },
            "清楚、温和、喜欢做简短记录",
            "按日程整理资料并核对细节",
            "安静的阅读时间",
            "混乱和遗失的记录");

        public static NpcPersonaDefinition Momo { get; } = new NpcPersonaDefinition(
            "墨墨",
            "小镇维护员",
            new[] { "冷静", "观察敏锐", "有条理" },
            "简洁、偶尔带一点幽默",
            "沿固定路线巡查公共设施",
            "运转良好的公共空间",
            "无人处理的小故障");

        public string Name { get; }

        public string Role { get; }

        public IReadOnlyList<string> PersonalityTraits => personalityTraits;

        public string SpeakingStyle { get; }

        public string WorkHabit { get; }

        public string Preference { get; }

        public string Dislike { get; }

        public string PromptSummary =>
            $"你是{Name}，{Role}；{string.Join("、", personalityTraits)}；" +
            $"说话风格：{SpeakingStyle}；工作习惯：{WorkHabit}；" +
            $"喜欢{Preference}，不喜欢{Dislike}。";

        public static NpcPersonaDefinition ForResident(ResidentId residentId)
        {
            if (residentId == ResidentIds.Yaya)
            {
                return Yaya;
            }

            if (residentId == ResidentIds.Amu)
            {
                return Amu;
            }

            if (residentId == ResidentIds.Xiaosui)
            {
                return Xiaosui;
            }

            if (residentId == ResidentIds.Momo)
            {
                return Momo;
            }

            return Yaya;
        }
    }
}
