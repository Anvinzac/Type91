using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace RectifyPad.Classes
{
    public class DatabaseService
    {
        private const int MockSeedVersion = 10;
        private const int ThemeEditionSeedVersion = 1;
        private readonly SQLiteAsyncConnection _db;

        public DatabaseService()
        {
            string dbPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "rectifypad.db");
            _db = new SQLiteAsyncConnection(dbPath);
        }

        public async Task InitializeDatabaseAsync()
        {
            await _db.CreateTableAsync<LibraryShelf>();
            await _db.CreateTableAsync<Topic>();
            await _db.CreateTableAsync<CanvasPage>();
            await EnsureTopicColumnsAsync();
            await EnsurePageStyleColumnsAsync();
            await SeedMockVietnameseAsync();
            await SeedThemeEditionAsync();
        }

        public async Task SeedThemeEditionAsync()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            object stored = localSettings.Values["ThemeEditionSeedVersion"];
            int current = stored is int intValue ? intValue : stored is long longValue ? (int)longValue : 0;
            if (current >= ThemeEditionSeedVersion)
            {
                return;
            }

            var shelves = await _db.Table<LibraryShelf>().ToListAsync();
            int top = shelves.Count == 0 ? 0 : shelves.Min(s => s.SortIndex) - 1;
            var edition = await InsertLibraryAsync("Ấn bản thanh lịch", top);

            await InsertTopicAsync(
                edition.Id,
                "Một ngày trong ánh bạc",
                "Mười màn hình, từ sáng sớm đến nửa đêm",
                DateTime.Now,
                new[]
                {
                    Themed("Buổi sáng mở ra như một trang giấy chưa ai viết. Ánh sáng đến trước, chữ đến sau, tôi chờ câu đầu tiên tự tìm đường.", "Pearl", "Be Vietnam Pro", "SemiBold", "Center"),
                    Themed("Ly nước giữ một mảnh trời rất nhỏ. Tôi không uống vội, chỉ nhìn ánh sáng đi qua thủy tinh như một ý nghĩ trong veo.", "Mist", "Literata", "Normal", "Left"),
                    Themed("Gió trưa mang màu xanh của kính. Thành phố ngoài cửa sổ im như bản vẽ, đường nét thẳng và tiếng ồn ở rất xa.", "Glacier", "Be Vietnam Pro", "Normal", "Center"),
                    Themed("Chiều nghiêng trên bức tường đá ấm. Tôi viết chậm hơn, xóa nhiều hơn, vì chữ đẹp nhất thường ngắn và đầy khoảng lặng.", "Stone", "Fraunces", "Normal", "Right"),
                    Themed("Hoàng hôn không đỏ như người ta kể. Nó bạc dần, mỏng dần, như kim loại đánh bóng đến khi chỉ còn ánh phản chiếu.", "Platinum", "Be Vietnam Pro", "SemiBold", "Center"),
                    Themed("Đèn bật lên từng ô một. Căn phòng tối vừa đủ để chữ sáng hơn, tôi viết rõ nhất khi xung quanh im nhất.", "Titanium", "Literata", "SemiBold", "Left"),
                    Themed("Màn hình là ô cửa sổ không có gió. Tôi đặt vào đó một câu, rồi câu nữa, như xếp đá cuội bên bờ sông đêm.", "Space Gray", "Be Vietnam Pro", "Normal", "Center"),
                    Themed("Trời đêm xanh như mực chưa khô. Những vì sao không nói gì, chỉ đứng đúng chỗ, thế là đủ thành một bố cục hoàn hảo.", "Midnight Blue", "Cormorant Infant", "SemiBold", "Center"),
                    Themed("Gần nửa đêm, mọi thứ thừa đều đã ngủ. Chỉ còn lại một dòng chữ, một nhịp thở, và ánh sáng vừa đủ để không ai lạc.", "Graphite", "Be Vietnam Pro", "SemiBold", "Left"),
                    Themed("Tôi tắt màn hình. Bóng tối không xóa những gì đã viết, nó chỉ giữ chúng lại như một chiếc hộp đen tuyền cho buổi sáng mai.", "Obsidian", "Literata", "Normal", "Center")
                });

            localSettings.Values["ThemeEditionSeedVersion"] = ThemeEditionSeedVersion;
        }

        private static CanvasPage Themed(string text, string themeName, string fontFamily, string fontWeight, string alignment)
        {
            var theme = CanvasThemes.Find(themeName);
            return Page(text, fontFamily, "96", fontWeight, alignment, theme.Ink, theme.PaperHex, false, "1.1");
        }

        private async Task EnsurePageStyleColumnsAsync()
        {
            var info = await _db.GetTableInfoAsync("CanvasPages");
            var names = new HashSet<string>(info.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            await AddColumnIfMissingAsync(names, "FontFamily", "TEXT");
            await AddColumnIfMissingAsync(names, "FontSize", "TEXT");
            await AddColumnIfMissingAsync(names, "FontWeight", "TEXT");
            await AddColumnIfMissingAsync(names, "Alignment", "TEXT");
            await AddColumnIfMissingAsync(names, "TextColor", "TEXT");
            await AddColumnIfMissingAsync(names, "BackgroundColor", "TEXT");
            await AddColumnIfMissingAsync(names, "UseRoundedRect", "INTEGER");
            await AddColumnIfMissingAsync(names, "LineSpacing", "TEXT");
            await AddColumnIfMissingAsync(names, "ApplyToAll", "INTEGER");
            await AddColumnIfMissingAsync(names, "DecorativeMode", "INTEGER");
            await AddColumnIfMissingAsync(names, "WordDecorJson", "TEXT");
        }

        private async Task EnsureTopicColumnsAsync()
        {
            var info = await _db.GetTableInfoAsync("Topics");
            var names = new HashSet<string>(info.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            await AddTopicColumnIfMissingAsync(names, "LibraryId", "INTEGER");
        }

        private async Task AddTopicColumnIfMissingAsync(HashSet<string> names, string column, string type)
        {
            if (names.Contains(column))
            {
                return;
            }

            await _db.ExecuteAsync("ALTER TABLE Topics ADD COLUMN " + column + " " + type);
            names.Add(column);
        }

        private async Task AddColumnIfMissingAsync(HashSet<string> names, string column, string type)
        {
            if (names.Contains(column))
            {
                return;
            }

            await _db.ExecuteAsync("ALTER TABLE CanvasPages ADD COLUMN " + column + " " + type);
            names.Add(column);
        }

        public async Task SeedMockVietnameseAsync()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            object stored = localSettings.Values["MockSeedVersion"];
            int current = 0;
            if (stored is int intValue) current = intValue;
            else if (stored is long longValue) current = (int)longValue;
            if (current >= MockSeedVersion)
            {
                return;
            }

            await _db.DeleteAllAsync<CanvasPage>();
            await _db.DeleteAllAsync<Topic>();
            await _db.DeleteAllAsync<LibraryShelf>();
            localSettings.Values.Remove("ThemeEditionSeedVersion");

            var phoDem = await InsertLibraryAsync("Phố đêm", 0);
            var thuHe = await InsertLibraryAsync("Thư mùa hè", 1);
            var saiGon = await InsertLibraryAsync("Sài Gòn", 2);
            var thoRoi = await InsertLibraryAsync("Thơ rời", 3);

            await InsertTopicAsync(
                phoDem.Id,
                "Phố đêm Hà Nội",
                "Ánh đèn vàng trên phố cổ",
                DateTime.Now.AddDays(-4),
                new[]
                {
                    Page("Hà Nội về đêm không vội. Từng bước chân trên phố cổ như được phép chậm lại nghe gió thổi qua mái ngói.", "Fraunces", "96", "Bold", "Center", "Kem", "#1B1B2F", false, "1.1"),
                    Page("Phố Hàng Đào còn giữ hơi người. Những ô cửa đóng rồi mở mùi vải mới lẫn mùi trà đặc chưa đi hết.", "Cormorant Infant", "96", "SemiBold", "Left", "Kem", "#121212", false, "1.1"),
                    Page("Một chiếc xe đạp đi qua hồ Gươm như một dấu phẩy. Đèn vàng run trên mặt nước thành phố viết tiếp bằng im lặng.", "Literata", "96", "Normal", "Center", "Kem", "#0B3D4A", true, "1.1"),
                    Page("Quán trà nhỏ mở cửa rất khẽ. Người ngồi trong không nói nhiều chỉ nâng chén nhìn hơi nước bay lên rồi tan vào đêm.", "Fraunces", "96", "SemiBold", "Right", "Kem", "#3B2416", false, "1.1"),
                    Page("Tôi nhớ mùi lá ướt và tiếng rao xa. Có những ký ức không cần ảnh chỉ cần một góc phố người đi ngang.", "Cormorant Infant", "96", "Normal", "Left", "White", "#111111", false, "1.1"),
                    Page("Đèn lồng đỏ soi một góc ký ức. Ai đó cười rất nhẹ sau bức tường đủ để tôi đứng lại rồi lại đi.", "Be Vietnam Pro", "96", "Bold", "Center", "White", "#C45C26", false, "1.1"),
                    Page("Có những đêm chỉ cần đi chậm là đủ về nhà. Không cần đích đến rõ ràng chỉ cần phố còn đèn và tim còn nhớ.", "Literata", "96", "SemiBold", "Center", "Kem", "#1B1B2F", true, "1.1"),
                    Page("Hà Nội không ngủ chỉ im hơn một chút như người già ngồi tựa cửa nhìn tháng năm đi qua không gọi giữ.", "Fraunces", "96", "Bold", "Center", "White", "#121212", false, "1.1")
                });

            await InsertTopicAsync(
                thuHe.Id,
                "Thư gửi mùa hè",
                "Những dòng viết cho tháng sáu",
                DateTime.Now.AddDays(-3),
                new[]
                {
                    Page("Mùa hè ơi em có nhận được thư này không. Tôi viết từ buổi trưa có quạt kêu đều. Ngoài sân nắng trải mỏng.", "Literata", "96", "Normal", "Left", "Black", "#FFF6E8", false, "1.1"),
                    Page("Nắng trải dài như một tấm lụa cũ. Em bước qua bóng ngả xuống thềm tôi muốn giữ lại dù biết mùa hè không giữ gì lâu.", "Cormorant Infant", "96", "SemiBold", "Center", "Black", "#F4E3B2", true, "1.1"),
                    Page("Ve kêu từ trưa cho đến khi mái tôn nguội. Tiếng ve ấy không phải nhạc nhưng đủ để nhớ một cái sân dưới giàn mướp.", "Be Vietnam Pro", "96", "Normal", "Center", "Black", "#FAF4EC", false, "1.1"),
                    Page("Mẹ phơi áo trên dây trước sân. Gió vào áo bay tôi đứng nhìn rất lâu như nhìn một điều sắp biến mất vẫn muốn gọi bình yên.", "Literata", "96", "SemiBold", "Left", "Navy", "#E7F0E4", false, "1.1"),
                    Page("Em nhớ ly nước đá chanh và tiếng cười rất khẽ. Những thứ nhỏ ấy mùa hè mang đi rồi trả lại mỗi lần một ít mặn hơn.", "Fraunces", "96", "Normal", "Center", "Black", "#FFF6E8", true, "1.1"),
                    Page("Tháng sáu không hỏi gì cả. Chỉ đưa tay như bảo hãy ngồi xuống hãy uống nước hãy để nắng làm việc của nó.", "Cormorant Infant", "96", "Bold", "Right", "Black", "#F4E3B2", false, "1.1"),
                    Page("Nếu gặp một buổi chiều không mưa hãy nghĩ tới tôi. Không cần viết lại chỉ cần đứng dưới hiên nghe gió đi qua lá.", "Literata", "96", "Normal", "Left", "Navy", "#E7F0E4", false, "1.1"),
                    Page("Mùa hè giữ hộ những điều chưa nói. Tôi tin vậy vì mỗi năm nó lại đến mang theo mùi nắng và một chỗ trống quen thuộc.", "Be Vietnam Pro", "96", "SemiBold", "Center", "Black", "#FAF4EC", false, "1.1"),
                    Page("Thư này không cần trả lời. Chỉ cần đọc chậm rồi gấp lại như gấp một chiếc quạt giấy để dành cho ngày oi hơn.", "Cormorant Infant", "96", "Normal", "Center", "Navy", "#E8DFF5", true, "1.1")
                });

            await InsertTopicAsync(
                saiGon.Id,
                "Cà phê sáng Sài Gòn",
                "Hơi sữa, nắng sớm, xe máy",
                DateTime.Now.AddDays(-2),
                new[]
                {
                    Page("Sài Gòn thức dậy bằng mùi cà phê. Trước khi nắng tới vỉa hè người ta đã ngồi xuống mở nắp ly thành phố bắt đầu nói.", "Be Vietnam Pro", "96", "Bold", "Center", "Kem", "#3B2416", false, "1.1"),
                    Page("Ly sữa đá cầm hơi lạnh trong lòng bàn tay. Một ngụm đủ để nhớ đêm qua chưa ngủ hết và ngày mới đã đứng ngay trước mặt.", "Literata", "96", "SemiBold", "Left", "Black", "#FFF6E8", true, "1.1"),
                    Page("Xe máy tràn ra phố như một dòng sông không bao giờ cạn. Ai cũng có chỗ phải đến vẫn kịp nhìn nhau một cái ở đèn đỏ.", "Fraunces", "96", "Normal", "Center", "White", "#C45C26", false, "1.1"),
                    Page("Người bán bánh mì rao rất nhỏ nhưng ai cũng nghe. Mùi pate nóng len vào buổi sáng khiến đói trở thành lý do dễ chịu để ở lại.", "Be Vietnam Pro", "96", "SemiBold", "Left", "Kem", "#121212", false, "1.1"),
                    Page("Nắng chín giờ đã đủ để nhớ một ai đó. Không phải vì đẹp mà vì Sài Gòn lúc ấy vừa vội vừa mềm vẫn kịp cười.", "Cormorant Infant", "96", "Normal", "Center", "Black", "#F4E3B2", false, "1.1"),
                    Page("Ngồi lại một chút thành phố sẽ đợi. Cà phê chưa cạn thì chưa cần đi chuyện chưa nói vẫn có thể để trên bàn.", "Literata", "96", "Bold", "Right", "Kem", "#3B2416", true, "1.1"),
                    Page("Cà phê tan nhưng buổi sáng còn đọng. Tôi đứng dậy trả tiền bước vào nắng mang theo một vị đắng rất nhẹ ở cuối lưỡi.", "Be Vietnam Pro", "96", "SemiBold", "Center", "White", "#0B3D4A", false, "1.1")
                });

            await InsertTopicAsync(
                phoDem.Id,
                "Người đi qua mưa",
                "Truyện ngắn — mười màn hình",
                DateTime.Now.AddDays(-1),
                new[]
                {
                    Page("Cơn mưa bắt đầu từ một giọt rơi lệch mái hiên. Giọt thứ hai rơi đúng vai áo anh kéo cổ áo lên và bước tiếp.", "Literata", "96", "Normal", "Left", "Kem", "#0B3D4A", false, "1.1"),
                    Page("Anh không mang áo mưa chỉ mang một cái tên chưa ai gọi. Phố ướt làm đèn vàng dài ra như muốn giữ người đi chậm hơn một nhịp.", "Fraunces", "96", "SemiBold", "Center", "Kem", "#1B1B2F", true, "1.1"),
                    Page("Phố bóng nhẫy. Đèn vàng vỡ ra thành nghìn mảnh. Mỗi bước chân đều có tiếng nước nhỏ và đều như đồng hồ không mặt số.", "Be Vietnam Pro", "96", "Bold", "Center", "White", "#121212", false, "1.1"),
                    Page("Một người đàn bà đứng dưới mái che nhìn anh đi. Không phải vì quen mà vì mưa làm mọi người thành người quen trong giây lát.", "Cormorant Infant", "96", "Normal", "Left", "Navy", "#E8DFF5", false, "1.1"),
                    Page("Không ai gọi lại. Mưa cũng không. Anh nghĩ nếu có một tiếng gọi mình sẽ đứng nhưng phố chỉ có nước chảy xuống cống.", "Literata", "96", "SemiBold", "Center", "White", "#1B1B2F", false, "1.1"),
                    Page("Anh nghĩ về một căn phòng khô một ly nước ấm. Những thứ rất thường lúc này lại sáng như cửa sổ nhà ai vừa bật đèn.", "Fraunces", "96", "Normal", "Left", "Kem", "#3B2416", true, "1.1"),
                    Page("Bước chân ngày càng chậm như sợ về tới nơi. Về tới nơi thì mưa hết chuyện để kể anh phải đối diện im lặng trong nhà.", "Cormorant Infant", "96", "SemiBold", "Right", "Kem", "#0B3D4A", false, "1.1"),
                    Page("Mưa không xóa được những chữ chưa viết. Chúng ướt át hơn dính vào tay áo đi theo anh đến cuối phố như một lời chưa nói.", "Literata", "96", "Bold", "Center", "Black", "#FAF4EC", false, "1.1"),
                    Page("Cuối phố ánh sáng cửa hàng còn mở. Có người đang lau bàn có tiếng nhạc nhỏ anh đứng ngoài nhìn vào như nhìn một đời khác.", "Be Vietnam Pro", "96", "Normal", "Center", "White", "#C45C26", false, "1.1"),
                    Page("Anh đứng lại. Mưa vẫn đi. Thành phố không chờ ai nhưng có những phút giây nó cho phép một người được ướt và được nhớ.", "Fraunces", "96", "Bold", "Center", "White", "#111111", true, "1.1")
                });

            await InsertTopicAsync(
                thoRoi.Id,
                "Bài thơ rời",
                "Thơ viết chậm, chữ nổi trên giấy",
                DateTime.Now,
                new[]
                {
                    Page("Im lặng cũng là một câu. Người ta chỉ quên chấm hết nên nó kéo dài đến sáng đến khi có ai đó rót nước hỏi ngủ được không.", "Literata", "96", "Normal", "Left", "Black", "#FAF4EC", false, "1.1"),
                    Page("Em đứng đó như một dấu hỏi không cần trả lời. Tôi đi qua đi lại rồi hiểu ra có những câu hỏi đẹp hơn khi để trống.", "Cormorant Infant", "96", "SemiBold", "Center", "Navy", "#E8DFF5", true, "1.1"),
                    Page("Tôi viết hoa để nhớ mình từng lớn tiếng. Rồi chữ nhỏ lại vì có những điều chỉ cần thì thầm cũng đủ làm tối cửa sổ.", "Be Vietnam Pro", "96", "Bold", "Center", "Kem", "#1B1B2F", false, "1.1"),
                    Page("Những chữ nhỏ cũng đủ làm ướt mắt. Không cần bi kịch chỉ cần một cái tên viết sai chính tả trên tờ giấy nguệch ngoạc.", "Fraunces", "96", "Normal", "Left", "Black", "#FFF6E8", false, "1.1"),
                    Page("Bên phải là nơi mình giấu nhớ. Bên trái là nơi mình đặt tách cà phê ở giữa là khoảng lặng để thở rồi viết tiếp.", "Cormorant Infant", "96", "SemiBold", "Right", "Kem", "#3B2416", false, "1.1"),
                    Page("Căn giữa. Thở. Rồi đi tiếp. Thơ không cần kết chỉ cần một nhịp đủ để người đọc đặt sách xuống mà vẫn nghe thấy mưa.", "Literata", "96", "Bold", "Center", "Navy", "#E7F0E4", true, "1.1"),
                    Page("Màu mực không đen hơn những điều không nói. Tôi viết xóa viết lại và để lại khoảng trắng như một lời xin lỗi nhẹ nhàng.", "Be Vietnam Pro", "96", "Normal", "Center", "White", "#121212", false, "1.1"),
                    Page("Hết thơ nhưng chưa hết mưa trong ngực. Mai nếu nắng lên hãy coi đêm nay chỉ là một trang nháp viết nghiêng và chưa ký tên.", "Fraunces", "96", "SemiBold", "Center", "Black", "#FAF4EC", false, "1.1")
                });

            localSettings.Values["MockSeedVersion"] = MockSeedVersion;
        }

        private async Task<LibraryShelf> InsertLibraryAsync(string name, int sortIndex)
        {
            var shelf = new LibraryShelf { Name = name, SortIndex = sortIndex };
            await SaveLibraryAsync(shelf);
            return shelf;
        }

        private async Task InsertTopicAsync(int libraryId, string title, string description, DateTime created, CanvasPage[] pages)
        {
            var topic = new Topic
            {
                Title = title,
                Description = description,
                CreationDate = created,
                LibraryId = libraryId
            };
            await SaveTopicAsync(topic);

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].TopicId = topic.Id;
                pages[i].PageIndex = i;
                await SavePageAsync(pages[i]);
                topic.Pages.Add(pages[i]);
            }
        }

        private static CanvasPage Page(
            string text,
            string fontFamily,
            string fontSize,
            string fontWeight,
            string alignment,
            string textColor,
            string backgroundColor,
            bool rounded,
            string lineSpacing)
        {
            return new CanvasPage
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Alignment = alignment,
                TextColor = CanvasContrast.InkFor(backgroundColor, textColor),
                BackgroundColor = backgroundColor,
                UseRoundedRect = rounded,
                LineSpacing = lineSpacing,
                ApplyToAll = false
            };
        }

        public async Task<List<LibraryShelf>> GetLibraryTreeAsync()
        {
            var shelves = await _db.Table<LibraryShelf>().OrderBy(s => s.SortIndex).ToListAsync();
            var topics = await GetTopicsAsync();
            if (shelves.Count == 0 && topics.Count > 0)
            {
                var fallback = new LibraryShelf { Name = "Thư viện", SortIndex = 0 };
                await SaveLibraryAsync(fallback);
                foreach (var topic in topics)
                {
                    topic.LibraryId = fallback.Id;
                    await SaveTopicAsync(topic);
                    fallback.Topics.Add(topic);
                }

                shelves.Add(fallback);
                return shelves;
            }

            foreach (var shelf in shelves)
            {
                shelf.Topics.Clear();
                foreach (var topic in topics)
                {
                    if (topic.LibraryId == shelf.Id)
                    {
                        shelf.Topics.Add(topic);
                    }
                }
            }

            return shelves;
        }

        public async Task SaveLibraryAsync(LibraryShelf shelf)
        {
            if (shelf.Id == 0)
            {
                await _db.InsertAsync(shelf);
            }
            else
            {
                await _db.UpdateAsync(shelf);
            }
        }

        public async Task DeleteLibraryAsync(LibraryShelf shelf)
        {
            var topics = await _db.Table<Topic>().Where(t => t.LibraryId == shelf.Id).ToListAsync();
            foreach (var topic in topics)
            {
                await DeleteTopicAsync(topic);
            }

            await _db.DeleteAsync(shelf);
        }

        public async Task<List<Topic>> GetTopicsAsync()
        {
            var topics = await _db.Table<Topic>().OrderByDescending(t => t.CreationDate).ToListAsync();
            foreach (var topic in topics)
            {
                var pages = await _db.Table<CanvasPage>()
                                     .Where(p => p.TopicId == topic.Id)
                                     .OrderBy(p => p.PageIndex)
                                     .ToListAsync();

                topic.Pages.Clear();
                if (pages.Count == 0)
                {
                    var newPage = new CanvasPage { TopicId = topic.Id, PageIndex = 0 };
                    await SavePageAsync(newPage);
                    topic.Pages.Add(newPage);
                }
                else
                {
                    foreach (var p in pages) topic.Pages.Add(p);
                }
            }
            return topics;
        }

        public async Task SaveTopicAsync(Topic topic)
        {
            if (topic.Id == 0)
            {
                await _db.InsertAsync(topic);
            }
            else
            {
                await _db.UpdateAsync(topic);
            }
        }

        public async Task SavePageAsync(CanvasPage page)
        {
            if (page.Id == 0)
            {
                await _db.InsertAsync(page);
            }
            else
            {
                await _db.UpdateAsync(page);
            }
        }

        public async Task DeletePageAsync(CanvasPage page)
        {
            if (page != null)
            {
                await _db.DeleteAsync(page);
            }
        }

        public async Task DeleteTopicAsync(Topic topic)
        {
            var pages = await _db.Table<CanvasPage>().Where(p => p.TopicId == topic.Id).ToListAsync();
            foreach (var page in pages)
            {
                await _db.DeleteAsync(page);
            }
            await _db.DeleteAsync(topic);
        }
    }
}
