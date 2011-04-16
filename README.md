Quick and dirty bit of C# to:

1. Read a list of articles from the Bozeman Daily Chronicle's RSS feed
2. Determine if any of them are new articles
3. Grab the story out of any new articles
4. Send an email containing the story the specified addresses
5. Record the sent stories

Example Command Line:

    BdcScraper.exe "http://www.bozemandailychronicle.com/search/?q=&t=&l=60&d=&d1=&d2=&s=start_time&sd=desc&c[]=news&f=rss" you@gmail.com you@gmail.com your.mail.server
