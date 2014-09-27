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
    Console.WriteLine("Requesting index from " + rssUrl);
    var rss = GetUrl(rssUrl);
    XmlDocument xml = new XmlDocument();
    xml.LoadXml(WhackGeoRss(WhackBadEntities(RemoveBadControlCharacters(rss))));
    XmlNodeList items = xml.SelectNodes("//item");
    Console.WriteLine("Found " + items.Count + " possible articles.");
    foreach (XmlNode item in items) {
      string link = item.SelectSingleNode("link").InnerText;
      string title = item.SelectSingleNode("title").InnerText;
      if (!link.Contains("bozemandailychronicle")) {
        Console.WriteLine("Ignoring '{0}' from '{1}' since I only want articles from the BDC.", title, link);
        continue;
      }
      if (!link.EndsWith(".html")) {
        Console.WriteLine("Ignoring '{0}' from '{1}' since I only want HTML.", title, link);
        continue;
      }
      if (link.Contains("/news/state/")) {
        Console.WriteLine("Ignoring '{0}' from '{1}' since I don't want the Chronicle's state coverage.", title, link);
        continue;
      }
      if (link.Contains("/poll_") || link.Contains("/youtube_") || link.Contains("/image_")) {
        Console.WriteLine("Ignoring '{0}' from '{1}' since I don't want entries that are just polls, images or videos.", title, link);
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
      Console.WriteLine("Headline:");
      Console.WriteLine(story.Headline);
      Console.WriteLine("Body:");
      Console.WriteLine(story.Body);
      Console.WriteLine("^");
      Console.WriteLine("End Body");
      Console.WriteLine();
      Console.WriteLine();
    }
  }

  private static void PrintUsage() {
    Console.WriteLine("USAGE <rssUrl> <toEmailAddresses> <fromEmailAddress> <mailServer>");
    Console.WriteLine("");
  }

  private static string RemoveBadControlCharacters(string xml) {
    return Regex.Replace(xml, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
  }

  // The feed included georss namespaced tags without a declaration of of the namespace, which made the parser sad.
  private static string WhackGeoRss(string xml) {
    return Regex.Replace(xml, @"(</?)georss:([^>]+>)", @"$1$2");
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
    LookingForImages,
    LookingForGalleryOrByline,
    ReadingGallery,
    ReadingByline,
    LookingForContent,
    LookingForContentEnd,
    LookingForEncrypted,
    ReadingEncrypted,
  }

  private static void AddLine(string line, StringBuilder body) {
    line = Regex.Replace(line, "&#822[01]([^;])", delegate(Match match) { return match.Groups[1].Value; }).Trim();
    Console.WriteLine("    In state " + state + " adding line: " + line);
    body.AppendLine(line);
  }

  private static string DecryptLine(string line) {
    string unescaped = line.
      Trim().
      Replace("&gt;", ">").
      Replace("&lt;", "<").
      Replace("&amp;", "&").
      Replace("&quot;", "\"");
    StringBuilder b = new StringBuilder();
    foreach (char c in unescaped) {
      if (c < '!' || c > '~') {
        b.Append(c);
      } else {
        b.Append(DecryptChar(c));
      }
    }
    return b.ToString();
  }

  private static char DecryptChar(char c) {
    return (char)(((c + 14) % 94) + 33);
  }

  private static ParseState state;

  private static ParseState Transition(ParseState newState, string line) {
    Console.WriteLine("Transitioning to " + newState + " on line: " + line);
    state = newState;
    return newState;
  }

  private static string ExtractStoryBodyFrom(string page, string url) {
    Transition(ParseState.LookingForGalleryOrByline, "BeginningOfStory");
    StringBuilder body = new StringBuilder();
    using (StringReader reader = new StringReader(page)) {
      string line;
      while ((line = reader.ReadLine()) != null) {
        switch (state) {
        case ParseState.LookingForGalleryOrByline:
          if (line.Contains("class=\"byline\"")) {
            Transition(ParseState.ReadingByline, line);
          } else if (line.Contains("class=\"instant-gallery\"")) {
            Transition(ParseState.ReadingGallery, line);
          }
          break;
        case ParseState.ReadingGallery:
          if (line.Contains("class=\"preview-slide-navigator\"")) {
            AddLine("<br><br>", body);
            Transition(ParseState.LookingForGalleryOrByline, line);
          } else if (line.Contains("<img")) {
            AddLine(line, body);
          }
          break;
        case ParseState.ReadingByline:
          AddLine(line, body);
          if (line.Contains("class=\"author ") || line.Contains("id=\"blox-story-text\"")) {
            AddLine("<br><br>", body);
            Transition(ParseState.LookingForContent, line);
          }
          break;
        case ParseState.LookingForContent:
          if (line.Contains("class=\"content\"")) {
            Transition(ParseState.LookingForContentEnd, line);
          }
          break;
        case ParseState.LookingForContentEnd:
          if (line.Contains("tncms-restricted-notice") || line.Contains("subscription-notice") || line.Contains("<!-- (END) Pagination Content Wrapper -->")) {
            Transition(ParseState.LookingForEncrypted, line);
          } else {
            AddLine(line, body);
          }
          break;
        case ParseState.LookingForEncrypted:
          if (line.Contains("class=\"encrypted-content\"")) {
            Transition(ParseState.ReadingEncrypted, line);
          }
          break;
        case ParseState.ReadingEncrypted:
          if (line.Contains("</div>")) {
            Transition(ParseState.LookingForEncrypted, line);
          } else {
            AddLine(DecryptLine(line), body);
          }
          break;
        }
      }
    }
    string capturedBody = body.ToString();
    if (capturedBody.Length < 100) {
      return string.Format("<h1>Parse failure while scraping</h1><br>Failed to parse the page at <a href=\"{0}\">{1}</a>.  Parsing failed while in the {2} state.");
      //throw new Exception("Didn't parse correctly: " + url + " " + state + "\n" + page);
    }
    return capturedBody;
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
