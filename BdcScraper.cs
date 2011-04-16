using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Generic;

public class BdcScraper {
    public static void Main(string[] args) {
        if (args.Length != 4) {
            PrintUsage();
            return;
        }

        var rssUrl = args[0];
        var toAddresses = args[1];
        var fromAddress = args[2];
        var mailServer = args[3];

        PastArticles pastArticles = new PastArticles(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase));
        //var rssUrl = "http://www.bozemandailychronicle.com/search/?q=&t=article&l=10&d=&d1=&d2=&s=start_time&sd=desc&c[]=news&f=rss";
        Console.WriteLine("Requesting index from " + rssUrl);
        var rss = GetUrl(rssUrl);
        XmlDocument xml = new XmlDocument();
        xml.LoadXml(WhackBadEntities(rss));
        XmlNodeList items = xml.SelectNodes("//item");
        Console.WriteLine("Found " + items.Count + " possible articles.");
        foreach (XmlNode item in items) {
            string link = item.SelectSingleNode("link").InnerText;
            string title = item.SelectSingleNode("title").InnerText;
            if (link.Contains("/news/state/")) {
                Console.WriteLine("Ignoring '{0}' from '{1}' since I don't want the Chronicle's state coverage.", title, link);
                continue;
            }
            if (link.Contains("/image_")) {
                Console.WriteLine("Ignoring '{0}' from '{1}' since I don't want entries that are just images.", title, link);
                continue;
            }
            if (!pastArticles.IsNew(link)) {
                Console.WriteLine("Found '" + title + "' from " + link + " but ignoring it since it looks like old news.");
                continue;
            }
            Console.WriteLine("Getting story '" + title + "' from " + link);
            Story story = GetStory(link, title);
            SendStory(story, toAddresses, fromAddress, mailServer);
            pastArticles.Insert(link);
            Console.WriteLine(story.Headline);
            Console.WriteLine(story.Body);
            Console.WriteLine();
            Console.WriteLine();
        }
    }

    private static void PrintUsage() {
        Console.WriteLine("USAGE <rssUrl> <toEmailAddresses> <fromEmailAddress> <mailServer>");
        Console.WriteLine("");
    }

    private static string WhackBadEntities(string xml) {
        return Regex.Replace(xml, @"&(?!(quot|amp|apos|lt|gt|#(\d{1,5}|x[\da-fA-F]{1,4}));)(.|$)", @"&amp;$3", RegexOptions.Multiline);
    }

    private static string Deentityize(string s) {
        return s.Replace("&#036;", "$").Replace("&#36;", "$").Replace("&#8217;", "'").Replace("&#8220;", "\"").Replace("&#8221;", "\"").Replace("&rsquo;", "'").Replace("&lsquo;", "'");
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

    private static void SendStory(Story story, string toAddresses, string fromAddress, string mailServer) {
        MailMessage message = new MailMessage(fromAddress, toAddresses, string.Format("BozemanDailyChronicle: {0}" , story.Headline), story.Body);
        message.IsBodyHtml = true;
        SmtpClient client = new SmtpClient(mailServer);
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
                    if (line.Contains("blox-comments") || line.Contains("<!-- begin comment tabs") || line.Contains("<!-- bottom html") || line.Contains("story-tools-sprite")) {
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
        historyPath = path + Path.DirectorySeparatorChar + "PastArticles.history";
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
