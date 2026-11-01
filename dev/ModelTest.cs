using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using RitoChat;

class ModelTest
{
    static JavaScriptSerializer ser = new JavaScriptSerializer();
    static string knowQuestion = null;
    static string apiBase, model;
    static int fails = 0;

    static void Ok(string name, bool cond, string detail)
    {
        Console.WriteLine((cond ? "PASS  " : "FAIL  ") + name + "  " + detail);
        if (!cond) fails++;
    }

    static void Main(string[] args)
    {
        try
        {
            string mode = args[0];
            apiBase = args.Length > 1 ? args[1] : "http://127.0.0.1:11500/v1";
            model = args.Length > 2 ? args[2] : "qwen2.5:7b";
            if (mode == "unit") UnitTests();
            else if (mode == "fake") FakeChatTests();
            else if (mode == "model") ModelPosts(args[3], int.Parse(args[4]));
            else if (mode == "fix") FixExisting(args[3]);
        else if (mode == "fixone") FixOne(args[3], args[4]);
        else if (mode == "scan") Scan(args[3]);
        else if (mode == "chat") ChatEmoji(args[3], args.Length > 4 ? args[4] : null);
        else if (mode == "group") GroupChat(args[3]);
        else if (mode == "vary") VaryTest(args[3]);
        else if (mode == "short") ShortTest(args[3]);
        else if (mode == "len") LengthTest(args[3]);
        else if (mode == "style") StyleTest(args[3]);
        else if (mode == "know") { knowQuestion = args.Length > 6 ? args[6] : null; KnowTest(args[3], args[4], args.Length > 5 ? args[5] : null); }
        else if (mode == "loop") LoopTest(args[3], args[4]);
        else if (mode == "ai") AiFeelTest(args[3], args[4]);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(ModelTest).Assembly.Location), "err-" + args[0] + ".txt"), ex.GetType().FullName + "\n" + ex.Message + "\n" + ex.StackTrace); } catch { }
            fails++;
        }
        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static Character Ch(string id) { Character c = new Character(); c.id = id; c.name = id; c.prompt = "你是" + id + "。"; return c; }

    static Func<List<Dictionary<string, object>>, int, double, string> Fixed(string text)
    {
        return delegate(List<Dictionary<string, object>> m, int mt, double t) { return text; };
    }

    static void GroupChat(string charsPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Dictionary<string, Character> byId = new Dictionary<string, Character>();
        foreach (Character c in all) byId[c.id] = c;
        List<Character> mem = new List<Character>();
        string[] ids = { "lala", "yui", "yami" };
        List<string> names = new List<string>();
        foreach (string id in ids)
        {
            Character c;
            if (byId.TryGetValue(id, out c)) { mem.Add(c); names.Add(c.name); }
        }
        Console.WriteLine("群成员：" + string.Join("、", names.ToArray()));
        List<string> hist = new List<string>();
        hist.Add("小条：晚上有空吗");
        hist.Add("拉拉：有空有空！");
        var msgs = MainForm.GroupMessages(mem, hist, "那一起去吃拉面吧，谁要去？");
        string reply = null;
        try { reply = Call(msgs, 320, 0.85); } catch (Exception ex) { Console.WriteLine("call failed: " + ex.Message); }
        Console.WriteLine("--- 模型原始输出 ---");
        Console.WriteLine(reply);
        var seg = MainForm.ParseGroupReply(reply, names);
        Console.WriteLine("--- 拆成 " + seg.Count + " 条 ---");
        foreach (var kv in seg) Console.WriteLine("[" + kv.Key + "] " + kv.Value);
        Ok("群聊至少拆出 2 条成员发言", seg.Count >= 2, "" + seg.Count);
        bool allMember = true;
        foreach (var kv in seg) if (!names.Contains(kv.Key)) allMember = false;
        Ok("发言人都属于群成员", allMember, "");
    }

    // 多轮聊天，看表情是不是换着用（不总同一个）
    static void VaryTest(string charsPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "yui") c = x;
        if (c == null) c = all[0];
        string[] turns = { "今天好累", "我在写作业", "你吃了吗", "周末想干嘛" };
        List<string> recent = new List<string>();
        List<string> seen = new List<string>();
        int withEmoji = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(MsgOf("system", c.prompt));
            msgs.Add(MsgOf("system", "【表情】可以在回复里加微信表情，一条最多两个；要换着用，不要每次都用同一个（😊 😅 😳 😴 😂 🥲 😑 🙄 🥺 😏 🤔 😩 都很常用），也可以不用，别连着发一排。"));
            msgs.Add(MsgOf("user", t));
            string raw = Call(msgs, 220, 0.75);
            string reply = MainForm.VaryEmoji(raw, recent, 2);
            List<string> es = MainForm.ExtractEmojis(reply);
            if (es.Count > 0) withEmoji++;
            foreach (string e in es) { recent.Add(e); if (!seen.Contains(e)) seen.Add(e); }
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [表情 " + (es.Count == 0 ? "无" : string.Join("", es.ToArray())) + "]");
        }
        Ok("有表情的轮次", withEmoji >= 1, withEmoji + "/" + turns.Length);
        Ok("出现过不同表情（不是一直同一个）", seen.Count >= 2 || withEmoji <= 1, "不同表情 " + seen.Count + " 个：" + string.Join("", seen.ToArray()));
    }

    // 收话：用户回得很短/敷衍时，AI 不应该一直追问、硬找话题
    static void ShortTest(string charsPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "yui") c = x;
        if (c == null) c = all[0];
        string[] turns = { "嗯", "哦", "我在忙", "先不说了" };
        List<Dictionary<string, object>> hist = new List<Dictionary<string, object>>();
        hist.Add(MsgOf("system", c.prompt));
        hist.Add(MsgOf("system", "【收话】别老想着把话题接下去：一两句就够，可以只回「嗯」「哦」「好」「行吧」这种短的，说完就停；不用每句都追问或反问，一次最多问一个问题，也可以完全不问；小条回得很短、像在忙，你就顺势收住，别硬聊、别硬换新话题。"));
        int bad = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>(hist);
            msgs.Add(MsgOf("user", t));
            string reply = Call(msgs, 160, 0.75);
            reply = MainForm.VaryEmoji(reply, new List<string>(), 2);
            int q = MainForm.CountQuestions(reply);
            if (q >= 2) bad++;
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [问号 " + q + " 个, 长度 " + reply.Length + "]");
            hist.Add(MsgOf("user", t));
            hist.Add(MsgOf("assistant", reply));
        }
        Ok("敷衍时不要连着问（没有一条问号 ≥2）", bad == 0, bad + " 条不合格");
    }

    // 长短：闲聊要短，大事可以长
    static void LengthTest(string charsPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "yui") c = x;
        if (c == null) c = all[0];

        string[][] cases = new string[][] {
            new string[] { "今天中午吃了拉面，挺好吃的", "短" },
            new string[] { "下午要去图书馆", "短" },
            new string[] { "我刚下体育课，好累", "短" },
            new string[] { "我生病了，今天一直发烧，特别难受，头也疼，什么也吃不下，你说我该怎么办", "长" }
        };
        int shortBad = 0;
        foreach (string[] cs in cases)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(MsgOf("system", c.prompt));
            msgs.Add(MsgOf("system", "【收话】别老想着把话题接下去：一两句就够，可以只回「嗯」「哦」「好」「行吧」这种短的，说完就停；不用每句都追问或反问，一次最多问一个问题，也可以完全不问；小条回得很短、像在忙，你就顺势收住，别硬聊、别硬换新话题。"));
            msgs.Add(MsgOf("system", "【长短】这是打字聊天，正常就一两句、一共 10～35 个字，像你一句我一句发消息，不要分段、不要写成小作文；只有真碰上大事或严肃的事（生病受伤、家里出事、吵架道歉、重要决定、很担心小条）才允许一次说三到五句，这种情况要少见。"));
            msgs.Add(MsgOf("user", cs[0]));
            string reply = Call(msgs, 240, 0.75);
            reply = MainForm.VaryEmoji(reply, new List<string>(), 2);
            bool serious = MainForm.IsSeriousMessage(cs[0]);
            bool tooLong = !serious && reply.Length > 60;
            if (tooLong) shortBad++;
            Console.WriteLine("[" + cs[1] + "] 小条：" + cs[0]);
            Console.WriteLine("     " + c.name + "：" + reply + "   [" + reply.Length + " 字, 换行 " + (reply.Contains("\n") ? "有" : "无") + "]");
        }
        Ok("闲聊回复不长（都 ≤60 字）", shortBad == 0, shortBad + " 条太长");
    }

    // 语气：是不是像打字聊天，而不是写报告
    static void StyleTest(string charsPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "zastin") c = x;
        if (c == null) c = all[0];
        string[] turns = { "最近看了全职猎人，感觉挺有意思的", "我比较喜欢西索吧", "你最近在忙什么" };
        List<Dictionary<string, object>> hist = new List<Dictionary<string, object>>();
        hist.Add(MsgOf("system", c.prompt));
        hist.Add(MsgOf("system", "【收话】别老想着把话题接下去：一两句就够，可以只回「嗯」「哦」「好」「行吧」这种短的，说完就停；不用每句都追问或反问，一次最多问一个问题，也可以完全不问；小条回得很短、像在忙，你就顺势收住，别硬聊、别硬换新话题。"));
        hist.Add(MsgOf("system", "【长短】这是打字聊天，正常就一两句、一共 10～35 个字，像你一句我一句发消息，不要分段、不要写成小作文；只有真碰上大事或严肃的事才允许一次说三到五句。"));
        hist.Add(MsgOf("system", "【说话】用平时打字的口气，说人话：短句、口语，可以有「啊」「欸」「算了」「说真的」这种词和省略号；不要书面语和成语堆砌（比如“事务繁多”“感到肩上的负担沉重”“剧情紧凑，人物设定也挺吸引人”这种一律不要），不要写完整的书面句子、不要总结或点评、不要像解说；打个比方：说「哦，那部我也看过，挺带劲的，你最喜欢谁？」就对了，说「那是一部不错的漫画，我也看过，剧情紧凑」就错了；不知道的事就说不知道，不要编设定，也不要替别人编喜好。"));
        int bad = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>(hist);
            msgs.Add(MsgOf("user", t));
            string reply = MainForm.VaryEmoji(Call(msgs, 240, 0.75), new List<string>(), 2);
            bool bookish = reply.Contains("事务") || reply.Contains("负担") || reply.Contains("设定") ||
                           reply.Contains("而言之") || reply.Contains("吸引人") || reply.Contains("颇为") || reply.Contains("而言");
            if (bookish || reply.Length > 60) bad++;
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [" + reply.Length + " 字" + (bookish ? ", 偏书面" : "") + "]");
            hist.Add(MsgOf("user", t));
            hist.Add(MsgOf("assistant", reply));
        }
        Ok("语气像聊天（不书面、不长）", bad == 0, bad + " 条有问题");
    }

    // 知识卡片：注入后仍然短、口语
    static void KnowTest(string charsPath, string knowPath, string charId)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Dictionary<string, string> know = ser.Deserialize<Dictionary<string, string>>(File.ReadAllText(knowPath, Encoding.UTF8));
        Character c = null;
        if (!string.IsNullOrEmpty(charId)) foreach (Character x in all) if (x.id == charId) c = x;
        if (c == null) foreach (Character x in all) if (x.id == "yami") c = x;
        if (c == null) c = all[0];
        string card = "";
        know.TryGetValue(c.id, out card);
        if (card == null) card = "";
        Console.WriteLine("知识卡：" + card.Length + " 字（角色 " + c.name + "）");
        string[] turns;
        if (knowQuestion != null) turns = new string[] { knowQuestion };
        else if (c.id == "nemesis") turns = new string[] { "你现在住在哪", "你和谁共用一个身体" };
        else turns = new string[] { "你今天吃了什么", "你喜欢什么", "最近在忙什么" };
        int bad = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(MsgOf("system", c.prompt));
            msgs.Add(MsgOf("system", "【你的知识】" + MainForm.ClipKnowledge(card, 700) + "\n只在小条聊到相关话题时才用这些，不要主动科普、不要背书、不要一次说完；说错了就承认，别编新的设定。"));
            msgs.Add(MsgOf("system", "【收话】一两句就够，说完就停，一次最多问一个问题。"));
            msgs.Add(MsgOf("system", "【长短】打字聊天，正常一两句、10～35 个字，不要分段、不要写小作文。"));
            msgs.Add(MsgOf("system", "【说话】短句、口语；不要书面语和成语堆砌，不要总结或点评。"));
            msgs.Add(MsgOf("user", t));
            string reply = MainForm.VaryEmoji(Call(msgs, 240, 0.75), new List<string>(), 2);
            bool tooLong = reply.Length > 60;
            if (tooLong) bad++;
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [" + reply.Length + " 字" + (tooLong ? ", 超长" : "") + "]");
        }
        Ok("有知识卡时仍然简短", bad == 0, bad + " 条超长");
    }

    // 复读：连续敷衍回应时，AI 不应循环同一句话
    static void LoopTest(string charsPath, string knowPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Dictionary<string, string> know = ser.Deserialize<Dictionary<string, string>>(File.ReadAllText(knowPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "momo") c = x;
        if (c == null) c = all[0];
        string card = "";
        know.TryGetValue(c.id, out card);
        if (card == null) card = "";

        List<Dictionary<string, object>> hist = new List<Dictionary<string, object>>();
        hist.Add(MsgOf("system", c.prompt));
        hist.Add(MsgOf("system", "【你的知识】" + card));
        hist.Add(MsgOf("system", "【收话】一两句就够，说完就停，一次最多问一个问题。"));
        hist.Add(MsgOf("system", "【长短】打字聊天，正常一两句、10～35 个字，不要分段。"));
        hist.Add(MsgOf("system", "【说话】短句、口语；不要书面语，不要总结或点评。"));
        hist.Add(MsgOf("system", "【别复读】不要重复自己以前说过的句子；别每句都用「好的，」开头；别老是反问「怎么样」「你觉得如何」「要不要」；小条说「不错/可以/挺好的」就是答应了，你要往下走一步，说个具体的；没话说的时候就说自己这边的小事或心情，别把话丢回给他。"));

        string[] turns = { "很不错", "挺不错的", "可以呀" };
        List<string> prevReplies = new List<string>();
        int bad = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>(hist);
        if (MainForm.IsFlatAgreement(t)) hist.Add(MsgOf("system", "小条这句只是应了一声（“" + t + "”），没有别的话。你这条别提问、也别再提新安排：说一句你自己这边的小事或心情，说完就停。"));
            msgs.Add(MsgOf("user", t));
            string reply = Call(msgs, 200, 0.8);
            if (MainForm.IsRepeating(reply, prevReplies, 0.72))
            {
                Console.WriteLine("     [检测到复读，重写] 原：" + reply);
                List<Dictionary<string, object>> r1 = new List<Dictionary<string, object>>(msgs);
                r1.Add(MsgOf("assistant", reply));
                r1.Add(MsgOf("system", "你刚才已经说过几乎一样的话：" + (prevReplies.Count > 0 ? prevReplies[0] : "") + "\n这次必须换：直接回应小条这句话，给出新的、具体的反应或安排；不要重复上面那句，不要再用「好的，」开头，也不要再问「怎么样」「你觉得如何」。"));
                string rr = Call(r1, 200, 0.8);
                if (!string.IsNullOrEmpty(rr)) reply = rr;
            }
            if (prevReplies.Count > 0 && MainForm.IsRepeating(reply, prevReplies, 0.72)) bad++;
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [" + reply.Length + " 字]");
            prevReplies.Insert(0, reply);
            hist.Add(MsgOf("user", t));
            hist.Add(MsgOf("assistant", reply));
        }
        Ok("连续敷衍时没有在复读", bad == 0, bad + " 条重复");
    }

    // AI 感检查：抄例句、每条都带表情、每句都反问
    static void AiFeelTest(string charsPath, string knowPath)
    {
        List<Character> all = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Dictionary<string, string> know = ser.Deserialize<Dictionary<string, string>>(File.ReadAllText(knowPath, Encoding.UTF8));
        Character c = null;
        foreach (Character x in all) if (x.id == "nemesis") c = x;
        if (c == null) c = all[0];
        string card = "";
        know.TryGetValue(c.id, out card);
        if (card == null) card = "";

        List<Dictionary<string, object>> hist = new List<Dictionary<string, object>>();
        hist.Add(MsgOf("system", c.prompt));
        hist.Add(MsgOf("system", "【你的知识】" + card));
        hist.Add(MsgOf("system", "【表情】表情只是偶尔调味：一条最多一个，大部分消息干脆不带；连着几条都挂表情很假，也要换着用。"));
        hist.Add(MsgOf("system", "【收话】一两句就够，说完就停，一次最多问一个问题，也可以完全不问。"));
        hist.Add(MsgOf("system", "【长短】打字聊天，正常一两句、10～35 个字，不要分段。"));
        hist.Add(MsgOf("system", "【说话】用平时打字的口气，短句、口语；不要书面语和成语堆砌，不要完整的书面句子，不要总结或点评。"));
        hist.Add(MsgOf("system", "【别像AI】不要复述或总结小条刚说的话，别把他的词再念一遍；不要每句都用反问收尾；少用「确实」「其实」「不过」「总之」这类词，也少用「哈哈」开头。"));
        hist.Add(MsgOf("system", "【别复读】不要重复自己说过的话；别每句都用「好的，」开头；别老反问「怎么样」「你觉得如何」。"));
        if (!string.IsNullOrEmpty(card)) { }

        string[] turns = { "最近在看全职猎人", "我比较喜欢西索", "就是感觉他挺有意思的，幽默" };
        List<string> prevReplies = new List<string>();
        int emojiMsgs = 0, askedCount = 0, leaks = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>(hist);
            msgs.Add(MsgOf("user", t));
            string reply = Call(msgs, 240, 0.75);
            if (MainForm.IsRepeating(reply, prevReplies, 0.72))
            {
                List<Dictionary<string, object>> r1 = new List<Dictionary<string, object>>(msgs);
                r1.Add(MsgOf("assistant", reply));
                r1.Add(MsgOf("system", "你刚才已经说过几乎一样的话，这次换一句：直接回应小条这句，不要重复。"));
                string rr = Call(r1, 200, 0.8);
                if (!string.IsNullOrEmpty(rr)) reply = rr;
            }
            // 反问收尾 → 重写一次，仍不行就删问句
            if (MainForm.CountQuestions(t) == 0 && MainForm.CountQuestions(reply) >= 1)
            {
                List<Dictionary<string, object>> rq = new List<Dictionary<string, object>>(msgs);
                rq.Add(MsgOf("assistant", reply));
                rq.Add(MsgOf("system", "小条这句不是问句。你别反问他、也别把话丢回去：说一句你自己的反应或者你这边的事，说完就停，不要问号。"));
                string rq2 = Call(rq, 160, 0.7);
                if (!string.IsNullOrEmpty(rq2) && MainForm.CountQuestions(rq2) < MainForm.CountQuestions(reply)) reply = rq2;
                if (MainForm.CountQuestions(reply) >= 1) reply = MainForm.DropQuestions(reply, 0);
            }
            reply = MainForm.StripStrayLatin(reply);
            // 表情控制：最近两条都带过表情，这条就不带
            int withEmoji = 0;
            for (int i = 0; i < prevReplies.Count && i < 3; i++) if (MainForm.EmojiCount(prevReplies[i]) > 0) withEmoji++;
            reply = MainForm.LimitEmoji(reply, withEmoji >= 2 ? 0 : 1);
            reply = reply.Trim();
            if (MainForm.EmojiCount(reply) > 0) emojiMsgs++;
            if (MainForm.CountQuestions(reply) > 0) askedCount++;
            if (reply.Contains("挺带劲的")) leaks++;
            Console.WriteLine("小条：" + t);
            Console.WriteLine(c.name + "：" + reply + "   [" + reply.Length + " 字, 表情 " + MainForm.EmojiCount(reply) + ", 问号 " + MainForm.CountQuestions(reply) + "]");
            prevReplies.Insert(0, reply);
            hist.Add(MsgOf("user", t));
            hist.Add(MsgOf("assistant", reply));
        }
        Ok("没有照抄提示里的例句", leaks == 0, leaks + " 条");
        Ok("表情不是每条都带", emojiMsgs <= 2, emojiMsgs + "/3 条带表情");
        Ok("不是每句都反问", askedCount <= 2, askedCount + "/3 条带问号");
    }

    static void UnitTests()
    {
        Ok("含“小条”判为私聊", MainForm.LooksLikePrivateTalk("今天体育课后整个人都散了架。小条，你在干嘛呢？"), "");
        Ok("对话式“你在干嘛”判为私聊", MainForm.LooksLikePrivateTalk("今天风好大，你在干嘛呢"), "");
        Ok("问句结尾判为私聊", MainForm.LooksLikePrivateTalk("明天要不要一起去？"), "");
        Ok("正常动态不算私聊", !MainForm.LooksLikePrivateTalk("今天吃了碗拉面，汤头很浓。"), "");
        Ok("正常动态不算私聊2", !MainForm.LooksLikePrivateTalk("累死了，只想躺着。"), "");
        string s1 = MainForm.StripPrivateTalk("今天累死了。小条，你在干嘛呢？");
        Ok("删掉私聊句只留自己的", s1 == "今天累死了。", s1);
        string s2 = MainForm.StripPrivateTalk("小条你呢？");
        Ok("全是私聊句时删空", s2 == "", "[" + s2 + "]");
        string s3 = MainForm.StripPrivateTalk("今天下雨了，心情有点闷");
        Ok("没收尾标点会补句号", s3 == "今天下雨了，心情有点闷。", s3);

        List<string> avoid = new List<string>();
        avoid.Add("今天在便利店买到最后一个布丁，开心。");
        Ok("整句相同算撞车", MainForm.TooSimilar("今天在便利店买到最后一个布丁，开心。", avoid), "");
        Ok("开头相同算撞车", MainForm.TooSimilar("今天在便利店买到最后一个布丁，然后分给妹妹。", avoid), "");
        Ok("不同内容不算撞车", !MainForm.TooSimilar("下课后去操场跑了两圈，风很凉。", avoid), "");
        Ok("对照表为空不算撞车", !MainForm.TooSimilar("随便什么", null), "");

        string sp = MainForm.LimitEmoji("今天好累😅😴😭，早点睡", 2);
        Ok("表情数量被限制为 2", sp == "今天好累😅😴，早点睡", sp);
        Ok("没有表情时原样返回", MainForm.LimitEmoji("纯中文，没有表情", 1) == "纯中文，没有表情", "");
        Ok("能正确数表情", MainForm.EmojiCount("😅😴😭") == 3, "" + MainForm.EmojiCount("😅😴😭"));
        Ok("中文标点不算表情", MainForm.EmojiCount("好累。早点睡！") == 0, "" + MainForm.EmojiCount("好累。早点睡！"));
        string filler = new string('a', 219) + "😊";
        string cut = MainForm.CleanMoment(filler);
        Ok("超长截断不会把表情切成半个", !char.IsHighSurrogate(cut[cut.Length - 1]) && cut.Length <= 221, "len=" + cut.Length);

        // 表情抽取 / 换着用
        Ok("抽得出两个表情", MainForm.ExtractEmojis("好累😅早点睡😴").Count == 2, "" + MainForm.ExtractEmojis("好累😅早点睡😴").Count);
        Ok("没表情抽出来是空", MainForm.ExtractEmojis("没有表情的一句话").Count == 0, "");
        var recent1 = new List<string>(); recent1.Add("😅");
        string v1 = MainForm.VaryEmoji("今天好累😅", recent1, 2);
        Ok("和上次一样就换掉", MainForm.ExtractEmojis(v1).Count == 1 && MainForm.ExtractEmojis(v1)[0] != "😅", v1);
        var recent2 = new List<string>(); recent2.Add("😴");
        string v2 = MainForm.VaryEmoji("今天好累😅", recent2, 2);
        Ok("上次不同就保留", v2 == "今天好累😅", v2);
        string v3 = MainForm.VaryEmoji("好累😅😴😭", new List<string>(), 2);
        Ok("上限仍然生效（最多2个）", MainForm.EmojiCount(v3) == 2, v3);
        string v4 = MainForm.VaryEmoji("今天挺顺利的", new List<string>(), 2);
        Ok("没表情不会硬加", v4 == "今天挺顺利的", v4);
        var recentMany = new List<string>(); recentMany.Add("😅"); recentMany.Add("😴");
        string v5 = MainForm.VaryEmoji("好累😅", recentMany, 2);
        string picked = MainForm.ExtractEmojis(v5).Count > 0 ? MainForm.ExtractEmojis(v5)[0] : "";
        Ok("换出来的不是最近用过的", picked != "😅" && picked != "😴", v5);

        // 拼音首字母（通讯录排序用）
        Ok("拉拉→L", MainForm.PinyinInitial("拉拉") == "L", MainForm.PinyinInitial("拉拉"));
        Ok("结城梨斗→J", MainForm.PinyinInitial("结城梨斗") == "J", MainForm.PinyinInitial("结城梨斗"));
        Ok("古手川唯→G", MainForm.PinyinInitial("古手川唯") == "G", MainForm.PinyinInitial("古手川唯"));
        Ok("阿测→A", MainForm.PinyinInitial("阿测") == "A", MainForm.PinyinInitial("阿测"));
        Ok("小暗→X", MainForm.PinyinInitial("小暗") == "X", MainForm.PinyinInitial("小暗"));
        Ok("天条院沙姬→T", MainForm.PinyinInitial("天条院沙姬") == "T", MainForm.PinyinInitial("天条院沙姬"));
        Ok("西连寺春菜→X", MainForm.PinyinInitial("西连寺春菜") == "X", MainForm.PinyinInitial("西连寺春菜"));
        Ok("英文名首字母大写", MainForm.PinyinInitial("banana") == "B", MainForm.PinyinInitial("banana"));
        Ok("数字落到 #", MainForm.PinyinInitial("123") == "#", MainForm.PinyinInitial("123"));
        Ok("空名落到 #", MainForm.PinyinInitial("") == "#", MainForm.PinyinInitial(""));

        // 置顶排序：置顶的在前，其余保持原顺序
        Character p1 = Ch("p1"); p1.pinned = true;
        Character p2 = Ch("p2");
        Character p3 = Ch("p3"); p3.pinned = true;
        Character p4 = Ch("p4");
        var sorted = MainForm.SortForChatList(new List<Character>(new Character[] { p2, p1, p4, p3 }));
        string order = "";
        foreach (Character x in sorted) order += x.id;
        Ok("置顶排最前且顺序稳定", order == "p1p3p2p4", order);

        // 主动发消息的时机判断（毫秒）
        long nowMs = 1000000000000L;
        Ok("刚说过话不主动", !MainForm.ProactiveDue(nowMs, nowMs - 60000L, 0, 3, 8), "");
        Ok("闲置够了就主动", MainForm.ProactiveDue(nowMs, nowMs - 4 * 60000L, 0, 3, 8), "");
        Ok("两次主动间隔不够就不发", !MainForm.ProactiveDue(nowMs, nowMs - 30 * 60000L, nowMs - 2 * 60000L, 3, 8), "");
        Ok("间隔够了继续主动", MainForm.ProactiveDue(nowMs, nowMs - 30 * 60000L, nowMs - 9 * 60000L, 3, 8), "");
        Ok("没有记录时不急着发（从当下算起）", !MainForm.ProactiveDue(nowMs, 0, 0, 3, 8), "");

        // 收话：一句话里问几个问题
        Ok("两个问号算 2", MainForm.CountQuestions("你好吗？吃了吗？") == 2, "");
        Ok("全角半角都算", MainForm.CountQuestions("去哪?几点？") == 2, "");
        Ok("没问号算 0", MainForm.CountQuestions("嗯，好。") == 0, "");

        // 知识卡截断
        string longKnow = new string('k', 900);
        Ok("知识卡超长会截断", MainForm.ClipKnowledge(longKnow, 700).Length <= 701, "" + MainForm.ClipKnowledge(longKnow, 700).Length);
        Ok("短知识卡原样返回", MainForm.ClipKnowledge("短卡片", 700) == "短卡片", "");

        // 提问型消息允许答得长一点
        Ok("问怎么做算提问", MainForm.IsHowToQuestion("锅糊了怎么刷"), "");
        Ok("问能不能算提问", MainForm.IsHowToQuestion("猫能喝牛奶吗"), "");
        Ok("短招呼不算提问", !MainForm.IsHowToQuestion("在吗"), "");
        Ok("闲聊不算提问", !MainForm.IsHowToQuestion("今天好累"), "");

        // 复读检测
        string lineA = "好的，热饮和暖色调装饰，怎么样？";
        Ok("一模一样的句子相似度 1", MainForm.TextOverlap(lineA, lineA) > 0.99, "" + MainForm.TextOverlap(lineA, lineA));
        Ok("完全不同的句子相似度低", MainForm.TextOverlap("今天下雨了", lineA) < 0.4, "" + MainForm.TextOverlap("今天下雨了", lineA));
        var prevs = new List<string>();
        prevs.Add(lineA);
        Ok("重复上一句会被判定复读", MainForm.IsRepeating(lineA, prevs, 0.72), "");
        Ok("换了内容的句子不算复读", !MainForm.IsRepeating("那我把热饮换成姜茶", prevs, 0.72), "");
        var prevs2 = new List<string>();
        prevs2.Add("好的，冬日派对准备中，热饮和暖色调装饰，你觉得如何？");
        Ok("换个措辞继续反问也算复读", MainForm.IsRepeating("好的，热饮和暖色调装饰，怎么样？", prevs2, 0.72), "");

        // 敷衍式应声
        Ok("“很不错”算敷衍应声", MainForm.IsFlatAgreement("很不错"), "");
        Ok("“可以呀”算敷衍应声", MainForm.IsFlatAgreement("可以呀"), "");
        Ok("整句不算应声", !MainForm.IsFlatAgreement("我觉得这个主意挺好的，不过要提前订场地"), "");

        // 长短控制
        Ok("日常闲聊不算大事", !MainForm.IsSeriousMessage("今天好累啊"), "");
        Ok("生病算大事", MainForm.IsSeriousMessage("我生病了"), "");
        Ok("长段话算大事", MainForm.IsSeriousMessage(new string('a', 70)), "");
        Ok("按标点截断", MainForm.TrimToLength("第一句。第二句。第三句。", 5) == "第一句。", MainForm.TrimToLength("第一句。第二句。第三句。", 5));
        string cut2 = MainForm.TrimToLength("没有标点的很长很长很长很长很长一段话", 6);
        Ok("没标点时加省略号", cut2.EndsWith("…") && cut2.Length <= 8, cut2);

        // 群聊回复解析
        List<string> names = new List<string>();
        names.Add("拉拉"); names.Add("古手川唯"); names.Add("小暗");
        string raw = "拉拉：哈哈哈，梨斗又炸厨房了 😆\n古手川唯：真是的，太不像话了。\n路人甲：我也说两句\n没有冒号的一行\n小暗：……鲷鱼烧。";
        List<KeyValuePair<string, string>> seg = MainForm.ParseGroupReply(raw, names);
        Ok("群回复按「名字：内容」拆行", seg.Count == 3, "拆出 " + seg.Count + " 条");
        Ok("第一条说话人是拉拉", seg.Count > 0 && seg[0].Key == "拉拉", seg.Count > 0 ? seg[0].Key : "");
        Ok("非成员的发言被丢掉", seg.Count > 0 && seg[0].Value.Contains("炸厨房"), seg.Count > 0 ? seg[0].Value : "");
        Ok("没有冒号的行被忽略", seg.Count == 3, "");
        string many = "拉拉：1\n古手川唯：2\n小暗：3\n拉拉：4\n古手川唯：5\n小暗：6";
        Ok("最多保留 4 条", MainForm.ParseGroupReply(many, names).Count == 4, "" + MainForm.ParseGroupReply(many, names).Count);
        Ok("空回复解析为空", MainForm.ParseGroupReply("", names).Count == 0, "");

        // 人设摘要
        string shortP = MainForm.ShortPrompt("你现在扮演《出包王女》中的古手川唯，用第一人称和小条聊天。你是风纪委员，认真严格，其实很在意梨斗。");
        Ok("摘要去掉了开场白", shortP.StartsWith("你是风纪委员"), shortP);

        // 群提示词
        List<Character> mem = new List<Character>();
        mem.Add(Ch("拉拉")); mem.Add(Ch("古手川唯"));
        List<string> hist = new List<string>();
        hist.Add("小条：晚上吃什么");
        hist.Add("拉拉：我要吃布丁！");
        var gmsgs = MainForm.GroupMessages(mem, hist, "我做了咖喱");
        string sys = (string)gmsgs[0]["content"];
        string usr = (string)gmsgs[1]["content"];
        Ok("群提示词含成员名", sys.Contains("拉拉") && sys.Contains("古手川唯"), "");
        Ok("群提示词要求「角色名：内容」格式", sys.Contains("角色名：内容"), "");
        Ok("群上下文带历史和小条新消息", usr.Contains("我要吃布丁") && usr.Contains("我做了咖喱"), "");
        // ---- 话题识别（防止老聊同一个话题）----
        Ok("认出「吃的」话题", MainForm.TopicOf("今天蛋糕烤糊了") == 1, "" + MainForm.TopicOf("今天蛋糕烤糊了"));
        Ok("认出「学校」话题", MainForm.TopicOf("风纪委员要查校规") == 2, "" + MainForm.TopicOf("风纪委员要查校规"));
        Ok("认出「机械」话题", MainForm.TopicOf("这个齿轮装不上") == 4, "" + MainForm.TopicOf("这个齿轮装不上"));
        Ok("认出「植物」话题", MainForm.TopicOf("给绿萝浇水") == 5, "" + MainForm.TopicOf("给绿萝浇水"));
        Ok("认不出的话题给 0", MainForm.TopicOf("嗯") == 0, "" + MainForm.TopicOf("嗯"));
        // ---- 开头重复（「梨斗又在磨刀了…」这种）----
        Ok("开头一样算复读", MainForm.SamePrefix("梨斗又在磨刀了，真是的，刀伤到自己就不好了。你那边复习怎么样了？", "梨斗又在磨刀了，真是的，小心刀伤。测试。", 9), "");
        Ok("开头不同不算", !MainForm.SamePrefix("今天下雨了，带伞。", "梨斗又在磨刀了，真是的。", 9), "");
        Ok("短句不误判", !MainForm.SamePrefix("嗯。", "梨斗又在磨刀了。", 9), "");
        Ok("认出「感情」话题", MainForm.TopicOf("我对你有心动的感觉") == 15, "" + MainForm.TopicOf("我对你有心动的感觉"));
        Ok("认出「磨刀」算剑术类", MainForm.TopicOf("梨斗又在磨刀了") == 12, "" + MainForm.TopicOf("梨斗又在磨刀了"));
        Ok("空字符串给 0", MainForm.TopicOf("") == 0, "" + MainForm.TopicOf(""));
    }

    static void FakeChatTests()
    {
        Character c = Ch("测试");
        DateTime when = new DateTime(2026, 10, 31, 20, 0, 0);

        string r1 = MainForm.ComposePost(c, when, Fixed("今天在便利店买到最后一个布丁，开心。"), null);
        Ok("一次合格直接采用", r1 == "今天在便利店买到最后一个布丁，开心。", r1);

        int n = 0;
        string r2 = MainForm.ComposePost(c, when, delegate(List<Dictionary<string, object>> m, int mt, double t)
        {
            n++;
            return n == 1 ? "今天好累。小条，你在干嘛呢？" : "今天好累，回家路上买了个饭团。";
        }, null);
        Ok("私聊时触发重写并使用第二版", r2 == "今天好累，回家路上买了个饭团。", r2 + " (调用 " + n + " 次)");

        string r3 = MainForm.ComposePost(c, when, Fixed("今天下雨了，有点闷。小条你呢？"), null);
        Ok("两次都私聊则删句兜底", r3 == "今天下雨了，有点闷。", r3);

        string r4 = MainForm.ComposePost(c, when, Fixed("小条你在干嘛呢？"), null);
        Ok("全是私聊句则整条丢弃", r4 == "", "[" + r4 + "]");

        // 撞车重写：第一次抄了别人，第二次换了内容
        List<string> avoid = new List<string>();
        avoid.Add("今天体育课后整个人都散了架，只想赶紧躺着。");
        int k = 0;
        string r5 = MainForm.ComposePost(c, when, delegate(List<Dictionary<string, object>> m, int mt, double t)
        {
            k++;
            return k == 1 ? "今天体育课后整个人都散了架，只想赶紧躺着。" : "今天在图书室整理书架，发现一本旧漫画。";
        }, avoid);
        Ok("和别人撞车时重写", r5 == "今天在图书室整理书架，发现一本旧漫画。", r5 + " (调用 " + k + " 次)");
    }

    static void ModelPosts(string charsPath, int count)
    {
        List<Character> chars = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        if (chars == null || chars.Count == 0) { Console.WriteLine("没有角色"); fails++; return; }
        int n = Math.Min(count, chars.Count);
        int bad = 0, dup = 0;
        List<string> avoid = new List<string>();
        for (int i = 0; i < n; i++)
        {
            Character c = chars[i];
            string text = MainForm.ComposePost(c, DateTime.Now, delegate(List<Dictionary<string, object>> m, int mt, double t) { return Call(m, mt, t); }, avoid);
            bool looksBad = MainForm.LooksLikePrivateTalk(text);
            bool isDup = text.Length > 0 && avoid.Contains(text);
            if (looksBad) bad++;
            if (isDup) dup++;
            if (text.Length > 0) avoid.Add(text);
            Console.WriteLine((looksBad || isDup ? "BAD   " : "OK    ") + c.name + "：" + (text.Length == 0 ? "<空>" : text));
        }
        Ok("真模型生成 " + n + " 条，私聊味 0", bad == 0, "有问题 " + bad + " 条");
        Ok("真模型生成 " + n + " 条，没有重复", dup == 0, "重复 " + dup + " 条");
    }

    static void FixExisting(string momentsPath)
    {
        List<Moment> list = ser.Deserialize<List<Moment>>(File.ReadAllText(momentsPath, Encoding.UTF8));
        List<Character> chars = ser.Deserialize<List<Character>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(momentsPath), "characters.json"), Encoding.UTF8));
        Dictionary<string, Character> byId = new Dictionary<string, Character>();
        foreach (Character c in chars) byId[c.id] = c;

        string bak = momentsPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        File.Copy(momentsPath, bak, true);
        Console.WriteLine("备份: " + bak);

        List<string> avoid = new List<string>();
        foreach (Moment m in list) if (!MainForm.LooksLikePrivateTalk(m.text) && !string.IsNullOrEmpty(m.text)) avoid.Add(m.text);

        int changed = 0;
        foreach (Moment m in list)
        {
            if (!MainForm.LooksLikePrivateTalk(m.text)) continue;
            Character c;
            if (!byId.TryGetValue(m.charId, out c)) { Console.WriteLine("跳过（找不到角色）: " + m.charId); continue; }
            DateTime when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(m.ts).ToLocalTime();
            string oldText = m.text;
            string text = MainForm.ComposePost(c, when, delegate(List<Dictionary<string, object>> mm, int mt, double t) { return Call(mm, mt, t); }, avoid);
            if (text.Length == 0) { Console.WriteLine("重写为空，保持原样: " + oldText); continue; }
            m.text = text;
            avoid.Add(text);
            changed++;
            Console.WriteLine("[" + c.name + "]");
            Console.WriteLine("  旧: " + oldText);
            Console.WriteLine("  新: " + text);
        }
        if (changed > 0)
        {
            string tmp = momentsPath + ".tmp";
            File.WriteAllText(tmp, ser.Serialize(list), new UTF8Encoding(false));
            File.Replace(tmp, momentsPath, null);
        }
        Ok("重写了 " + changed + " 条私聊味动态", true, "");
    }

    static void Scan(string momentsPath)
    {
        List<Moment> list = ser.Deserialize<List<Moment>>(File.ReadAllText(momentsPath, Encoding.UTF8));
        int bad = 0;
        foreach (Moment m in list)
        {
            if (!MainForm.LooksLikePrivateTalk(m.text)) continue;
            bad++;
            Console.WriteLine("FLAG " + m.id + " [" + m.charId + "] " + m.text);
        }
        Ok("扫描 " + list.Count + " 条，命中 " + bad + " 条", true, "");
    }

    static void FixOne(string momentsPath, string id)
    {
        List<Moment> list = ser.Deserialize<List<Moment>>(File.ReadAllText(momentsPath, Encoding.UTF8));
        List<Character> chars = ser.Deserialize<List<Character>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(momentsPath), "characters.json"), Encoding.UTF8));
        Dictionary<string, Character> byId = new Dictionary<string, Character>();
        foreach (Character c in chars) byId[c.id] = c;

        List<string> avoid = new List<string>();
        foreach (Moment m in list) if (m.id != id && !string.IsNullOrEmpty(m.text)) avoid.Add(m.text);

        Moment target = null;
        foreach (Moment m in list) if (m.id == id) { target = m; break; }
        if (target == null) { Console.WriteLine("找不到 " + id); fails++; return; }

        Character who;
        if (!byId.TryGetValue(target.charId, out who)) { Console.WriteLine("找不到角色 " + target.charId); fails++; return; }

        DateTime when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(target.ts).ToLocalTime();
        string oldText = target.text;
        string text = MainForm.ComposePost(who, when, delegate(List<Dictionary<string, object>> mm, int mt, double t) { return Call(mm, mt, t); }, avoid);
        if (text.Length == 0) { Console.WriteLine("重写为空，保持原样"); fails++; return; }
        target.text = text;
        File.WriteAllText(momentsPath, ser.Serialize(list), new UTF8Encoding(false));
        Console.WriteLine("[" + who.name + "]");
        Console.WriteLine("  旧: " + oldText);
        Console.WriteLine("  新: " + text);
        Ok("重写 1 条", true, "");
    }

    static void ChatEmoji(string charsPath, string charId)
    {
        List<Character> chars = ser.Deserialize<List<Character>>(File.ReadAllText(charsPath, Encoding.UTF8));
        Character c = chars[4];
        if (!string.IsNullOrEmpty(charId))
        {
            foreach (Character x in chars) if (x.id == charId) { c = x; break; }
        }
        string[] turns = string.IsNullOrEmpty(charId)
            ? new string[] { "今天好累啊", "我都快散架了", "你晚饭吃了吗" }
            : new string[] { "最近怎么样", "你爸爸最近还好吗" };
        int withEmoji = 0;
        foreach (string t in turns)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(MsgOf("system", c.prompt));
            msgs.Add(MsgOf("system", "【表情】可以在回复里偶尔加一个微信表情（例如 😊 😅 😳 😴 🙄 😂），一条消息最多两个，不要每句都带，也不要连着发一排。"));
            msgs.Add(MsgOf("user", t));
            string reply = MainForm.LimitEmoji(Call(msgs, 200, 0.7), 2);
            int n = MainForm.EmojiCount(reply);
            if (n > 0) withEmoji++;
            Console.WriteLine("小条：" + t + "\n" + c.name + "：" + reply + "   [表情 " + n + " 个]");
        }
        Ok("3 轮聊天里至少 1 轮带表情", withEmoji >= 1, withEmoji + "/3");
    }

    static Dictionary<string, object> MsgOf(string role, string content)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["role"] = role; d["content"] = content;
        return d;
    }

    static string Call(List<Dictionary<string, object>> msgs, int maxTokens, double temperature)
    {
        Dictionary<string, object> payload = new Dictionary<string, object>();
        payload["model"] = model;
        payload["messages"] = msgs;
        payload["temperature"] = temperature;
        payload["max_tokens"] = maxTokens;
        payload["stream"] = false;
        byte[] data = Encoding.UTF8.GetBytes(ser.Serialize(payload));
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiBase.TrimEnd('/') + "/chat/completions");
        req.Method = "POST";
        req.ContentType = "application/json; charset=utf-8";
        req.Timeout = 180000;
        req.ReadWriteTimeout = 180000;
        req.ContentLength = data.Length;
        using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
        {
            Dictionary<string, object> o = (Dictionary<string, object>)ser.DeserializeObject(sr.ReadToEnd());
            IList choices = (IList)o["choices"];
            Dictionary<string, object> first = (Dictionary<string, object>)choices[0];
            Dictionary<string, object> msg = (Dictionary<string, object>)first["message"];
            return Convert.ToString(msg["content"]);
        }
    }
}
