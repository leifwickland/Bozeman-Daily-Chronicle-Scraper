using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Generic;

public class BdcRss {
    public static void Main(string[] args) {
        if (args.Length == 3) {
            SendSingleStory(args);
        }
        PastArticles pastArticles = new PastArticles(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase));
        string url = "http://www.bozemandailychronicle.com/search/?q=&t=article&l=10&d=&d1=&d2=&s=start_time&sd=desc&c[]=news&f=rss";
        Console.WriteLine("Requesting index from " + url);
        XmlDocument xml = new XmlDocument();
        xml.Load(url);
        XmlNodeList items = xml.SelectNodes("//item");
        Console.WriteLine("Found " + items.Count + " possible articles.");
        foreach (XmlNode item in items) {
            string link = item.SelectSingleNode("link").InnerText;
            string title = item.SelectSingleNode("title").InnerText;
            if (!pastArticles.IsNew(link)) {
                Console.WriteLine("Found '" + title + "' from " + link + " but ignoring it since it looks like old news.");
                continue;
            }
            Console.WriteLine("Getting story '" + title + "' from " + link);
            Story story = GetStory(link, title);
            SendStory(story);
            pastArticles.Insert(link);
            Console.WriteLine(story.Headline);
            Console.WriteLine(story.Body);
            Console.WriteLine();
            Console.WriteLine();
        }
    }

    private static string Deentityize(string s) {
        return s.Replace("&#036;", "$").Replace("&#36;", "$").Replace("&#8217;", "'").Replace("&#8220;", "\"").Replace("&#8221;", "\"").Replace("&rsquo;", "'").Replace("&lsquo;", "'");
    }

    private static void SendSingleStory(string[] args) {
        Console.WriteLine(args[0]);
        Console.WriteLine(args[1]);
        Console.WriteLine(args[2]);
        Story story = GetStory(args[0], args[1]);
        SendStory(story, args[2]);
        Console.WriteLine(story.Headline);
        Console.WriteLine(story.Body);
        Environment.Exit(0);
    }

    private static string GetUrl(string url) {
        WebRequest request = WebRequest.Create(url);
        Stream output = request.GetResponse().GetResponseStream();
        string page = new StreamReader(output).ReadToEnd();
        return page;
    }

    private static Story GetStory(string url, string headline) {
        Story story = new Story();
        story.Url = url;
        story.Headline = Deentityize(headline);
        story.Body = string.Format("<a href='{0}'>{1}</a><br>{2}", url, headline, Deentityize(ExtractStoryBodyFrom(GetUrl(url), url)));
        return story;
    }

    private static void SendStory(Story story) {
        SendStory(story, "leifwickland@gmail.com,elizabethwickland@gmail.com");
    }

    private static void SendStory(Story story, string recipients) {
        MailMessage message = new MailMessage("leifwickland@gmail.com", recipients, string.Format("BozemanDailyChronicle: {0}" , story.Headline), story.Body);
        message.IsBodyHtml = true;
        SmtpClient client = new SmtpClient("mail.bresnan.net");
        client.Credentials = null;
        client.Send(message);
    }

    private enum ParseState {
        LookingForHeadline,
            FoundHeadline,
            SkippingScript,
            LookingForEnd,
    }

    private static void AddLine(string line, StringBuilder body) {
        line = Regex.Replace(line, "&#822[01]([^;])", delegate(Match match) { return match.Groups[1].Value; });
        body.AppendLine(line.Trim());
    }

    private static string ExtractStoryBodyFrom(string page, string url) {
        StringBuilder body = new StringBuilder();
        ParseState state = ParseState.LookingForHeadline;
        using (StringReader reader = new StringReader(page)) {
            string line;
            while ((line = reader.ReadLine()) != null) {
                switch (state) {
                case ParseState.LookingForHeadline:
                    if (line.Contains("byline")) {
                        AddLine(line, body);
                        state = ParseState.LookingForEnd;
                    }
                    break;
                case ParseState.LookingForEnd:
                    if (line.Contains("<!-- bottom html") || line.Contains("story-tools-sprite")) {
                        return body.ToString();
                    }
                    if (line.Contains("<script")) {
                        state = ParseState.SkippingScript;
                        break;
                    }
                    AddLine(line, body);
                    break;
                case ParseState.SkippingScript:
                    if (line.Contains("</script")) {
                        state = ParseState.LookingForEnd;
                    }
                    break;
                }
            }
        }
        throw new Exception("Didn't parse correctly: " + url + " " + state + "\n" + page);
    }

    private class Story {
        public string Url;
        public string Headline;
        public string Body;
    }
}

class PastArticles {
    public PastArticles(string path) {
        historyPath = path + Path.DirectorySeparatorChar + "bdcRss.history";
        if (historyPath.StartsWith("file:"))
            historyPath = historyPath.Substring(5);
        Console.WriteLine("History path: " + historyPath);
        if (!File.Exists(historyPath)) {
            using (File.Create(historyPath)) {
                // nothing!
            }
        }
        list = new HashSet<string>(File.ReadAllLines(historyPath));
    }

    private string historyPath;
    private HashSet<string> list;

    public bool IsNew(string url) {
        return !list.Contains(url);
    }

    public void Insert(string url) {
        if (!list.Contains(url)) {
            list.Add(url);
            File.AppendAllText(historyPath, url + "\n");
        }
    }
}
