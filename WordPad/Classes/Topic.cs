using SQLite;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RectifyPad.Classes
{
    [Table("Topics")]
    public class Topic : INotifyPropertyChanged
    {
        private int _id;
        private string _title = "New Topic";
        private string _description = "A new topic";
        private DateTime _creationDate = DateTime.Now;
        private ObservableCollection<CanvasPage> _pages = new ObservableCollection<CanvasPage>();

        [PrimaryKey, AutoIncrement]
        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime CreationDate
        {
            get => _creationDate;
            set
            {
                if (_creationDate != value)
                {
                    _creationDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CreationDateString));
                }
            }
        }

        [Ignore]
        public string CreationDateString => _creationDate.ToString("d");

        [Ignore]
        public ObservableCollection<CanvasPage> Pages
        {
            get => _pages;
            set
            {
                if (_pages != value)
                {
                    if (_pages != null) _pages.CollectionChanged -= (s, e) => OnPropertyChanged(nameof(PageCount));
                    _pages = value;
                    if (_pages != null) _pages.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PageCount));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PageCount));
                }
            }
        }

        [Ignore]
        public int PageCount => _pages.Count;

        public int LibraryId
        {
            get => _libraryId;
            set
            {
                if (_libraryId != value)
                {
                    _libraryId = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _libraryId;

        public Topic()
        {
            _pages.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PageCount));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Table("LibraryShelves")]
    public class LibraryShelf : INotifyPropertyChanged
    {
        private int _id;
        private string _name = "Kệ mới";
        private int _sortIndex;
        private ObservableCollection<Topic> _topics = new ObservableCollection<Topic>();

        [PrimaryKey, AutoIncrement]
        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SortIndex
        {
            get => _sortIndex;
            set
            {
                if (_sortIndex != value)
                {
                    _sortIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        [Ignore]
        public ObservableCollection<Topic> Topics
        {
            get => _topics;
            set
            {
                if (_topics != value)
                {
                    _topics = value ?? new ObservableCollection<Topic>();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanvasCount));
                }
            }
        }

        [Ignore]
        public int CanvasCount => _topics?.Count ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
