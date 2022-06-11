using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Win32;

namespace UlinkToMarkup
{
    public class ULink
    {
        [JsonInclude]
        public string link, title, categoryID, description, liveBroadcast, thumbLink, durtion;
        [JsonInclude]
        public bool isPlaylist = false;
        [JsonInclude]
        public int publishedMonth = 1, publishedYear = 2000, itemCountPlaylist;
        [JsonInclude]
        public DateTime pubDate;
    }

    public class UCategory
    {
        [JsonInclude]
        public string categoryName;
        [JsonInclude]
        public List<ULink> links;

        public UCategory(string cName) { categoryName = cName; }
    }

    public class UData
    {
        [JsonInclude]
        public ULink latestLink;
        [JsonInclude]
        public List<UCategory> categories;   
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const Int32 BufferSize = 128;
        UData data = null;
        string filePath;
        string ut_api_key = "";
        bool isProcessing = false;
        int totalLinks = 0, currentLinkDone = 0;
        public MainWindow()
        {
            InitializeComponent();
            if (data == null)
            {
                data = new UData();
            }
            isProcessing = false;
        }
        
        private void OnOpenTextFileClick(object sender, RoutedEventArgs e)
        {
            if (isProcessing)
            {
                MessageBox.Show("Wait for current operation completion!");
                return;
            }
            filePath = SetFilePath();
            txtFilePathTbox.Text = IsInvalid(filePath) ? "Invalid or no Path! Won't process!!!" : filePath;
        }

        private async void OnProcessTextFile(object sender, RoutedEventArgs e)
        {
            if (isProcessing)
            {
                MessageBox.Show("Wait for current operation completion!");
                return;
            }

            if (IsInvalid(ut_api_key))
            {
                MessageBox.Show("Input a valid youtube api key!");
                return;
            }

            processBtn.Content = "Processing...";
            isProcessing = true;

            PreparePreUTData();
            if (data == null || data.categories == null || data.categories.Count == 0)
            {
                MessageBox.Show("No category data or invalid data!");
                isProcessing = false;
                return;
            }
            
            currentLinkDone = 0;
            for (int i = 0; i < data.categories.Count; i++)
            {
                var category = data.categories[i];
                if (category == null) { continue; }
                var links = category.links;
                if (links == null || links.Count == 0) { continue; }
                for (int j = 0; j < links.Count; j++)
                {
                    var link = links[j];
                    if (link == null) { continue; }
                    totalLinks++;
                }
            }
            await GetVideoDetails();
            await WriteDataToDevice();
            isProcessing = false;
            filePath = "";
            processBtn.Content = "Process";
        }

        string SetFilePath()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Show where is your youtube links";
            openFileDialog.Filter = "Text|*.txt|All|*.*";
            openFileDialog.Multiselect = false;
            var showDiaglog = openFileDialog.ShowDialog();
            if (showDiaglog == true)
            {
                var res = "";
                var fNames = openFileDialog.FileNames;
                if (fNames != null && fNames.Count() > 0)
                {
                    foreach (var f in fNames)
                    {
                        if (IsInvalid(f))
                        {
                            continue;
                        }
                        res = f;
                        break;
                    }
                }
                return res;
            }
            else
            {
                return null;
            }
        }

        bool IsInvalid(string str)
        {
            return string.IsNullOrEmpty(str) || string.IsNullOrWhiteSpace(str);
        }

        bool IsUTURL(string str)
        {
            return str.Contains("https://www.youtube.com");
        }

        bool IsUTPlaylistURL(string str)
        {
            return str.Contains("https://www.youtube.com/playlist?");
        }

        void PreparePreUTData()
        {
            if (data.categories == null) { data.categories = new List<UCategory>(); }

            using (var fileStream = File.OpenRead(filePath))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, true, BufferSize))
                {
                    String line;
                    UCategory curCategory = null;
                    while ((line = streamReader.ReadLine()) != null) 
                    {
                        if (IsInvalid(line)) { continue; }
                        if (IsUTURL(line))
                        {
                            //url processing
                            if (curCategory == null) { continue; }
                            if (curCategory.links == null) { curCategory.links = new List<ULink>(); }
                            var lnk = new ULink { link = line, isPlaylist = IsUTPlaylistURL(line) };
                            curCategory.links.Add(lnk);
                        }
                        else
                        {
                            //category start
                            curCategory = new UCategory(line);
                            data.categories.Add(curCategory);
                        }
                    }
                }
            }
        }

        private const string YoutubeLinkRegex = "(?:.+?)?(?:\\/v\\/|watch\\/|\\?v=|\\&v=|youtu\\.be\\/|\\/v=|^youtu\\.be\\/)([a-zA-Z0-9_-]{11})+";
        private static Regex regexExtractId = new Regex(YoutubeLinkRegex, RegexOptions.Compiled);
        private const string YoutubeLinkRegex_Playlist = "(?:.+?)?(?:\\/list\\/|playlist\\/|\\?list=|\\&list=|youtu\\.be\\/|\\/list=|^youtu\\.be\\/)([a-zA-Z0-9_-]{34})+";
        private static Regex regexExtractId_Playlist = new Regex(YoutubeLinkRegex_Playlist, RegexOptions.Compiled);
        private static string[] validAuthorities = { "youtube.com", "www.youtube.com", "youtu.be", "www.youtu.be" };

        string ExtractVideoIdFromUri(Uri uri)
        {
            try
            {
                string authority = new UriBuilder(uri).Uri.Authority.ToLower();

                //check if the url is a youtube url
                if (validAuthorities.Contains(authority))
                {
                    //and extract the id
                    var regRes = regexExtractId.Match(uri.ToString());
                    if (regRes.Success)
                    {
                        return regRes.Groups[1].Value;
                    }
                }
            }
            catch { }


            return null;
        }

        string ExtractPlaylistIdFromUri(Uri uri)
        {
            try
            {
                string authority = new UriBuilder(uri).Uri.Authority.ToLower();

                //check if the url is a youtube url
                if (validAuthorities.Contains(authority))
                {
                    //and extract the id
                    var regRes = regexExtractId_Playlist.Match(uri.ToString());
                    if (regRes.Success)
                    {
                        return regRes.Groups[1].Value;
                    }
                }
            }
            catch { }


            return null;
        }

        public async Task GetVideoDetails()
        {
            using (var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = ut_api_key,
            }))

                for (int i = 0; i < data.categories.Count; i++)
                {
                    var category = data.categories[i];
                    if (category == null) { continue; }
                    var links = category.links;
                    if (links == null || links.Count == 0) { continue; }
                    for (int j = 0; j < links.Count; j++)
                    {
                        var link = links[j];
                        if (link == null) { continue; }
                        var url = link.link;
                        if (link.isPlaylist)
                        {
                            var lst = new List<string>();
                            lst.Add("snippet");
                            lst.Add("contentDetails");
                            var plistRequest = youtubeService.Playlists.List(lst);
                            plistRequest.Id = ExtractPlaylistIdFromUri(new Uri(url));
                            var plistResponse = await plistRequest.ExecuteAsync();
                            var youTubePlaylist = plistResponse.Items.FirstOrDefault();

                            if (youTubePlaylist != null)
                            {
                                data.categories[i].links[j].title = youTubePlaylist.Snippet.Title;
                                data.categories[i].links[j].thumbLink = youTubePlaylist.Snippet.Thumbnails.Default__.Url;
                                data.categories[i].links[j].description = youTubePlaylist.Snippet.Description;
                                var pubDate = youTubePlaylist.Snippet.PublishedAt;
                                if (pubDate != null)
                                {
                                    data.categories[i].links[j].publishedMonth = pubDate.Value.Month;
                                    data.categories[i].links[j].publishedYear = pubDate.Value.Year;
                                    data.categories[i].links[j].pubDate = pubDate.Value;
                                }
                                else
                                {
                                    data.categories[i].links[j].publishedMonth = 1;
                                    data.categories[i].links[j].publishedYear = 1990;
                                }

                                if (youTubePlaylist.ContentDetails != null)
                                {
                                    var count = youTubePlaylist.ContentDetails.ItemCount;
                                    var countLong = count == null ? 0 : count.Value;
                                    data.categories[i].links[j].itemCountPlaylist = (int)(countLong);
                                }
                            }
                        }
                        else
                        {
                            var videoID = ExtractVideoIdFromUri(new Uri(url));
                            {
                                var lst = new List<string>();
                                lst.Add("snippet");
                                lst.Add("contentDetails");
                                var searchRequest = youtubeService.Videos.List(lst);//snippet,contentDetails
                                searchRequest.Id = videoID;
                                var searchResponse = await searchRequest.ExecuteAsync();
                                var youTubeVideo = searchResponse.Items.FirstOrDefault();
                                if (youTubeVideo != null)
                                {
                                    data.categories[i].links[j].title = youTubeVideo.Snippet.Title;
                                    data.categories[i].links[j].thumbLink = youTubeVideo.Snippet.Thumbnails.Default__.Url;
                                    data.categories[i].links[j].categoryID = youTubeVideo.Snippet.CategoryId;
                                    data.categories[i].links[j].description = youTubeVideo.Snippet.Description;
                                    data.categories[i].links[j].liveBroadcast = youTubeVideo.Snippet.LiveBroadcastContent;
                                    var pubDate = youTubeVideo.Snippet.PublishedAt;
                                    if (pubDate != null)
                                    {
                                        data.categories[i].links[j].publishedMonth = pubDate.Value.Month;
                                        data.categories[i].links[j].publishedYear = pubDate.Value.Year;
                                        data.categories[i].links[j].pubDate = pubDate.Value;
                                    }
                                    else
                                    {
                                        data.categories[i].links[j].publishedMonth = 1;
                                        data.categories[i].links[j].publishedYear = 1990;
                                    }

                                    if (youTubeVideo.ContentDetails != null)
                                    {
                                        var durationStr = youTubeVideo.ContentDetails.Duration;
                                        durationStr = durationStr.Replace("PT", "");
                                        data.categories[i].links[j].durtion = durationStr;
                                    }
                                }
                            }
                        }
                        
                        currentLinkDone++;
                        completionMsgTBox.Text = "Completed " + currentLinkDone + "/" + totalLinks;
                    }
                }

            var allLinks = new List<ULink>();
            for (int i = 0; i < data.categories.Count; i++)
            {
                var category = data.categories[i];
                if (category == null) { continue; }
                var links = category.links;
                if (links == null || links.Count == 0) { continue; }
                for (int j = 0; j < links.Count; j++)
                {
                    var link = links[j];
                    if (link == null) { continue; }
                    allLinks.Add(link);
                }
            }
            if (allLinks != null && allLinks.Count > 0)
            {
                allLinks = allLinks.OrderByDescending(o => o.pubDate).ToList();
                data.latestLink = allLinks[0];
            }
        }

        async Task WriteDataToDevice()
        {
            SaveFileDialog savefileDialog = new SaveFileDialog();
            savefileDialog.Title = "Show where to save the target markup file";
            savefileDialog.Filter = "Text|*.txt|All|*.*";
            var showDiaglog = savefileDialog.ShowDialog();
            if (showDiaglog == true && data != null && data.categories != null && data.categories.Count > 0)
            {
                var savePath = "";
                var fNames = savefileDialog.FileNames;
                if (fNames != null && fNames.Count() > 0)
                {
                    foreach (var f in fNames)
                    {
                        if (IsInvalid(f))
                        {
                            continue;
                        }
                        savePath = f;
                        break;
                    }
                }

                //sort the data by year and then by month
                for (int i = 0; i < data.categories.Count; i++)
                {
                    var cat = data.categories[i];
                    if (cat == null) { continue; }
                    var currentLinks = cat.links;
                    cat.links = cat.links.OrderByDescending(o => o.publishedYear).ThenByDescending((o => o.publishedMonth)).ToList();
                }

                //async file write of the data object
                WriteJson(savePath);

                string markupContent = "## A curated collection of Unreal Engine learning resources semi-compiled by an utility I made." +
                    System.Environment.NewLine +
                    "##### The tool can be found here in case you are interested. [ULinkToMarkup](https://github.com/kaiyumcg/ULinkToMarkup)" +
                    System.Environment.NewLine +
                    "##### Last generation date(Sorted by Date): " + DateTime.Now.ToString() +
                    System.Environment.NewLine +
                    "##### Note: Only related to mainstream game development. XR/AEC/Visual production etc are not considered(for now)." +
                    System.Environment.NewLine + System.Environment.NewLine;
                for (int i = 0; i < data.categories.Count; i++)
                {
                    var cat = data.categories[i];
                    if (cat == null) { continue; }
                    markupContent += System.Environment.NewLine + System.Environment.NewLine + System.Environment.NewLine +
                        "#### "+cat.categoryName;
                    var links = cat.links;
                    if (links != null && links.Count > 0)
                    {
                        for (int j = 0; j < links.Count; j++)
                        {
                            var link = links[j];
                            if (link == null) { continue; }
                            markupContent += System.Environment.NewLine +
                                (j + 1) + ". [" + link.title + (link.isPlaylist ? "[Playlist]" : "") +
                                (link.isPlaylist ? "[" + link.itemCountPlaylist + "]" : "[" + link.durtion + "]") +
                                "[" + GetMonthYear(link.publishedMonth, link.publishedYear) + "]" + "]" +
                                "(" + link.link + ")";

                        }
                    }
                }

                WriteTextAsync(savePath, markupContent);

                completionMsgTBox.Text = "Success!";
            }
            else
            {
               
                completionMsgTBox.Text = "FAIL!";
            }
        }

        string GetMonthYear(int month, int year)
        {
            switch (month)
            {
                case 1:
                    return "January " + year;
                case 2:
                    return "February " + year;
                case 3:
                    return "March " + year;
                case 4:
                    return "April " + year;
                case 5:
                    return "May " + year;
                case 6:
                    return "June " + year;
                case 7:
                    return "July " + year;
                case 8:
                    return "August " + year;
                case 9:
                    return "September " + year;
                case 10:
                    return "October " + year;
                case 11:
                    return "November " + year;
                case 12:
                    return "December " + year;
                default:
                    return "WTF";
            }
        }

        private void OnClickHelp(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/kaiyumcg/ULinkToMarkup");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        private void OnChangeTextOfAPIKey(object sender, TextChangedEventArgs e)
        {
            if (isProcessing)
            {
                return;
            }
            ut_api_key = apiKeyTxtBox.Text;
        }

        async Task WriteTextAsync(string filePath, string text)
        {
            byte[] encodedText = Encoding.UTF8.GetBytes(text);

            using (FileStream sourceStream = new FileStream(filePath,
                FileMode.Append, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
            };
        }

        async Task WriteJson(string savePathMain)
        {
            string fileName = savePathMain + ".json";
            using FileStream jsStream = File.Create(fileName);
            await JsonSerializer.SerializeAsync(jsStream, data);
            await jsStream.DisposeAsync();
        }
    }
}