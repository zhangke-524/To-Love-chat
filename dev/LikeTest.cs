using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using RitoChat;

class LikeTest
{
    static int fails = 0;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  " + detail);
        if (!ok) fails++;
    }

    static void Main(string[] args)
    {
        string oldJson = args[0];
        JavaScriptSerializer ser = new JavaScriptSerializer();

        Moment m = new Moment();
        m.id = "abc";
        m.likes = new List<string>();
        var mi = typeof(MainForm).GetMethod("ToggleLikeIn", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool a = (bool)mi.Invoke(null, new object[] { m, "user" });
        bool b = (bool)mi.Invoke(null, new object[] { m, "yui" });
        bool c = (bool)mi.Invoke(null, new object[] { m, "user" });
        Check("点赞切换", a && b && !c, "user=" + a + ", yui=" + b + ", 再点 user=" + c);
        Check("点赞名单", string.Join(",", m.likes.ToArray()) == "yui", "likes=" + string.Join(",", m.likes.ToArray()));

        string json = ser.Serialize(m);
        Check("写入 JSON 带 likes", json.Contains("\"likes\"") && json.Contains("yui"), json);

        string oldText = File.ReadAllText(oldJson);
        object[] arr = ser.DeserializeObject(oldText) as object[];
        Check("旧 moments.json 可解析", arr != null && arr.Length > 0, "条数=" + (arr == null ? -1 : arr.Length));
        Moment first = (Moment)ser.ConvertToType(arr[0], typeof(Moment));
        Check("旧数据 likes 为 null（LoadMoments 补空表）", first.likes == null, "likes=" + (first.likes == null ? "null" : "not-null"));
        bool d = (bool)mi.Invoke(null, new object[] { first, "user" });
        Check("旧数据点赞后写入正常", d && first.likes.Count == 1 && first.likes[0] == "user", "likes=" + string.Join(",", first.likes.ToArray()));

        Moment back = ser.Deserialize(json, typeof(Moment)) as Moment;
        Check("新数据回读", back.likes != null && back.likes.Count == 1 && back.likes[0] == "yui",
            "likes=" + (back.likes == null ? "null" : string.Join(",", back.likes.ToArray())));

        // 空 likes 的旧动态整条序列化再读回，确认 likes 字段不破坏其它字段
        Moment fresh = new Moment();
        fresh.id = "x1"; fresh.charId = "yui"; fresh.text = "测试"; fresh.ts = 1; fresh.date = "2026-10-31";
        fresh.comments = new List<MomentComment>();
        string j2 = ser.Serialize(fresh);
        Moment f2 = ser.Deserialize(j2, typeof(Moment)) as Moment;
        Check("无点赞动态序列化正常", f2 != null && f2.text == "测试" && f2.charId == "yui", j2);

        Console.WriteLine(fails == 0 ? "ALL PASS" : (fails + " FAILED"));
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
