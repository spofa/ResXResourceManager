﻿namespace tomenglertde.ResXManager.Model
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    using tomenglertde.ResXManager.Model.Properties;

    /// <summary>
    /// Represents a logical resource file, e.g. "Resources".
    /// A logical resource entity is linked to multiple physical resource files, one per langueage, e.g. "Resources.resx", "Resources.de.resx", "Resources.fr.resx".
    /// For windows store apps "de\Resources.resm", "en-us\Resources.resm" are also supported.
    /// </summary>
    public class ResourceEntity : IComparable<ResourceEntity>, IComparable, IEquatable<ResourceEntity>
    {
        private readonly IDictionary<string, ResourceLanguage> _languages;
        private readonly ResourceManager _owner;
        private readonly string _projectName;
        private readonly string _baseName;
        private readonly string _directory;
        private readonly ObservableCollection<ResourceTableEntry> _resourceTableEntries;
        private readonly string _displayName;
        private readonly string _relativePath;
        private readonly string _sortKey;

        internal ResourceEntity(ResourceManager owner, string projectName, string baseName, string directory, ICollection<ProjectFile> files)
        {
            Contract.Requires(owner != null);
            Contract.Requires(!String.IsNullOrEmpty(projectName));
            Contract.Requires(!String.IsNullOrEmpty(baseName));
            Contract.Requires(!String.IsNullOrEmpty(directory));
            Contract.Requires(files != null);

            _owner = owner;
            _projectName = projectName;
            _baseName = baseName;
            _directory = directory;
            _relativePath = GetRelativePath(directory, files);
            _displayName = projectName + @" - " + _relativePath + baseName;
            _sortKey = string.Concat(@" - ", _displayName, _directory);

            var languageQuery =
                from file in files
                let languageName = file.GetLanguageName()
                orderby languageName
                select new ResourceLanguage(languageName, file);

            _languages = languageQuery.ToDictionary(language => language.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var language in _languages.Values)
            {
                Contract.Assume(language != null);

                language.Changed += language_Changed;
                language.Changing += language_Changing;
            }

            var entriesQuery = _languages.Values.SelectMany(language => language.Keys)
                .Distinct()
                .OrderBy(key => key.ToUpper(CultureInfo.CurrentCulture))
                .Select(key => new ResourceTableEntry(this, key, _languages));

            _resourceTableEntries = new ObservableCollection<ResourceTableEntry>(entriesQuery);

            Contract.Assume(_languages.Any());
        }

        private static string GetRelativePath(string directory, IEnumerable<ProjectFile> files)
        {
            Contract.Requires(!String.IsNullOrEmpty(directory));
            Contract.Requires(files != null);
            Contract.Ensures(Contract.Result<string>() != null);

            var uniqueProjectName = files.Select(file => file.UniqueProjectName).FirstOrDefault();

            if (uniqueProjectName == null)
                return string.Empty;

            directory += Path.DirectorySeparatorChar;

            var subFolder = Path.DirectorySeparatorChar + Path.GetDirectoryName(uniqueProjectName) + Path.DirectorySeparatorChar;
            var pos = directory.LastIndexOf(subFolder, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
                return string.Empty;

            pos += subFolder.Length;

            Contract.Assume(pos <= directory.Length);
            var relativePath = directory.Substring(pos);

            return relativePath;
        }

        public event EventHandler<LanguageChangingEventArgs> LanguageChanging;
        public event EventHandler<LanguageChangedEventArgs> LanguageChanged;

        public ResourceManager Owner
        {
            get
            {
                Contract.Ensures(Contract.Result<ResourceManager>() != null);
                return _owner;
            }
        }

        /// <summary>
        /// Gets the containing project name of the resource entity.
        /// </summary>
        public string ProjectName
        {
            get
            {
                Contract.Ensures(Contract.Result<string>() != null);
                return _projectName;
            }
        }

        /// <summary>
        /// Gets the base name of the resource entity.
        /// </summary>
        public string BaseName
        {
            get
            {
                Contract.Ensures(Contract.Result<string>() != null);
                return _baseName;
            }
        }

        public string RelativePath
        {
            get { return _relativePath; }
        }

        public string DisplayName
        {
            get { return _displayName; }
        }

        /// <summary>
        /// Gets the directory where the physical files are located.
        /// </summary>
        public string Directory
        {
            get
            {
                Contract.Ensures(Contract.Result<string>() != null);
                return _directory;
            }
        }

        /// <summary>
        /// Gets the available languages of this resource entity.
        /// </summary>
        public ICollection<ResourceLanguage> Languages
        {
            get
            {
                Contract.Ensures(Contract.Result<IEnumerable<ResourceLanguage>>() != null);
                Contract.Ensures(Contract.Result<IEnumerable<ResourceLanguage>>().Any());
                return _languages.Values;
            }
        }

        /// <summary>
        /// Gets all the entries of this resource entity.
        /// </summary>
        public IList<ResourceTableEntry> Entries
        {
            get
            {
                Contract.Ensures(Contract.Result<IList<ResourceTableEntry>>() != null);

                return _resourceTableEntries;
            }
        }

        /// <summary>
        /// Removes the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Remove(ResourceTableEntry item)
        {
            Contract.Requires(item != null);

            foreach (var language in _languages.Values)
            {
                Contract.Assume(language != null);
                language.RemoveKey(item.Key);
            }

            _resourceTableEntries.Remove(item);
        }

        /// <summary>
        /// Adds an item with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        public ResourceTableEntry Add(string key)
        {
            Contract.Requires(!string.IsNullOrEmpty(key));

            var firstLanguage = _languages.First().Value;
            Contract.Assume(firstLanguage != null);
            firstLanguage.ForceValue(key, string.Empty); // force an entry in the neutral language resource file.
            var resourceTableEntry = new ResourceTableEntry(this, key, _languages);
            _resourceTableEntries.Add(resourceTableEntry);

            return resourceTableEntry;
        }

        public ResourceTableEntry AddNewKey()
        {
            var keyTemplate = Resources.AddNewItemTemplate;
            var key = keyTemplate;
            var index = 1;

            while (Entries.Any(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                key = string.Format(CultureInfo.InvariantCulture, "{0}_{1}", keyTemplate, index);
                index += 1;
            }

            return Add(key);
        }

        /// <summary>
        /// Adds the language represented by the specified file.
        /// </summary>
        /// <param name="file">The file.</param>
        public void AddLanguage(ProjectFile file)
        {
            Contract.Requires(file != null);

            var languageName = file.GetLanguageName();
            var language = new ResourceLanguage(languageName, file);

            language.Changed += language_Changed;
            language.Changing += language_Changing;

            _languages.Add(languageName, language);
        }

        public override string ToString()
        {
            return _displayName;
        }

        public bool CanEdit(CultureInfo culture)
        {
            if (LanguageChanging == null)
                return false;

            var args = new LanguageChangingEventArgs(this, culture);
            LanguageChanging(this, args);
            return !args.Cancel;
        }

        private void language_Changing(object sender, CancelEventArgs e)
        {
            if (LanguageChanging != null)
            {
                var language = (ResourceLanguage) sender;
                Contract.Assume(language != null);
                var args = new LanguageChangingEventArgs(this, language.Culture);
                LanguageChanging(this, args);
                e.Cancel = args.Cancel;
            }
        }

        private void language_Changed(object sender, EventArgs e)
        {
            if (LanguageChanged != null)
            {
                var language = (ResourceLanguage) sender;
                Contract.Assume(language != null);
                LanguageChanged(this, new LanguageChangedEventArgs(this, language));
            }
        }

        #region IComparable/IEquatable implementation

        private string SortKey
        {
            get
            {
                Contract.Ensures(Contract.Result<string>() != null);
                return _sortKey;
            }
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            return SortKey.GetHashCode();
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object"/> is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object"/> to compare with this instance.</param>
        /// <returns><c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as ResourceEntity);
        }

        /// <summary>
        /// Determines whether the specified <see cref="ResourceEntity" /> is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="ResourceEntity"/> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="ResourceEntity" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(ResourceEntity other)
        {
            return Compare(this, other) == 0;
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        public int CompareTo(object obj)
        {
            return Compare(this, obj as ResourceEntity);
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">An object to compare with this instance.</param>
        public int CompareTo(ResourceEntity other)
        {
            return Compare(this, other);
        }

        private static int Compare(ResourceEntity left, ResourceEntity right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (ReferenceEquals(left, null))
                return -1;
            if (ReferenceEquals(right, null))
                return 1;

            return string.Compare(left.SortKey, right.SortKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        public static bool operator ==(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) == 0;
        }

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        public static bool operator !=(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) != 0;
        }

        /// <summary>
        /// Implements the operator &gt;.
        /// </summary>
        public static bool operator >(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) > 0;
        }

        /// <summary>
        /// Implements the operator &lt;.
        /// </summary>
        public static bool operator <(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) < 0;
        }

        /// <summary>
        /// Implements the operator &gt;=.
        /// </summary>
        public static bool operator >=(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) >= 0;
        }

        /// <summary>
        /// Implements the operator &lt;=.
        /// </summary>
        public static bool operator <=(ResourceEntity left, ResourceEntity right)
        {
            return Compare(left, right) <= 0;
        }

        #endregion

        [ContractInvariantMethod]
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Required for code contracts.")]
        private void ObjectInvariant()
        {
            Contract.Invariant(_owner != null);
            Contract.Invariant(_languages != null);
            Contract.Invariant(_languages.Any());
            Contract.Invariant(_languages.Values.Any());
            Contract.Invariant(_resourceTableEntries != null);
            Contract.Invariant(!String.IsNullOrEmpty(_projectName));
            Contract.Invariant(!String.IsNullOrEmpty(_baseName));
            Contract.Invariant(!String.IsNullOrEmpty(_directory));
            Contract.Invariant(_displayName != null);
            Contract.Invariant(_relativePath != null);
            Contract.Invariant(_sortKey != null);
        }
    }
}