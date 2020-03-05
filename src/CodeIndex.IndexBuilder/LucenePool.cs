﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CodeIndex.Common;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace CodeIndex.IndexBuilder
{
    public class LucenePool
    {
        // TODO: refresh index reader and searcher when index changed

        internal static void BuildIndex(string luceneIndex, bool triggerMerge, bool applyAllDeletes, IEnumerable<Document> documents, bool needFlush)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var writer = CreateOrGetIndexWriter(luceneIndex);
                writer.AddDocuments(documents);

                if (needFlush)
                {
                    writer.Flush(triggerMerge, applyAllDeletes);
                }
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        internal static void DeleteIndex(string luceneIndex, params Query[] searchQueries)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var indexWriter = CreateOrGetIndexWriter(luceneIndex);
                indexWriter.DeleteDocuments(searchQueries);
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        internal static void DeleteIndex(string luceneIndex, params Term[] terms)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var indexWriter = CreateOrGetIndexWriter(luceneIndex);
                indexWriter.DeleteDocuments(terms);
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        internal static void UpdateIndex(string luceneIndex, Term term, Document document)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var indexWriter = CreateOrGetIndexWriter(luceneIndex);
                indexWriter.UpdateDocument(term, document);
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        internal static void DeleteAllIndex(string luceneIndex)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var indexWriter = CreateOrGetIndexWriter(luceneIndex);
                indexWriter.DeleteAll();
                indexWriter.Commit();
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }

            // TODO: Remove this logic
            SaveResultsAndClearLucenePool(luceneIndex);
        }

        public static void SaveResultsAndClearLucenePool(string luceneIndex)
        {
            try
            {
                readWriteLock.AcquireWriterLock(Constants.ReadWriteLockTimeOutMilliseconds);

                if (IndexReaderPool.TryRemove(luceneIndex, out var indexReader))
                {
                    indexReader.Dispose();
                }

                if (IndexWritesPool.TryRemove(luceneIndex, out var indexWriter))
                {
                    indexWriter.Dispose();
                }

                IndexSearcherPool.Clear();
            }
            finally
            {
                readWriteLock.ReleaseWriterLock();
            }
        }

        static object syncLockForWriter = new object();

        static IndexWriter CreateOrGetIndexWriter(string luceneIndex)
        {
            IndexWriter indexWriter;

            if (!IndexWritesPool.TryGetValue(luceneIndex, out indexWriter))
            {
                lock (syncLockForWriter)
                {
                    if (!IndexWritesPool.TryGetValue(luceneIndex, out indexWriter))
                    {
                        var dir = FSDirectory.Open(luceneIndex);
                        //create an analyzer to process the text
                        //create an index writer
                        var indexConfig = new IndexWriterConfig(Constants.AppLuceneVersion, GetAnalyzer());

                        indexWriter = new IndexWriter(dir, indexConfig);
                        IndexWritesPool.TryAdd(luceneIndex, indexWriter);
                    }
                }
            }

            return indexWriter;
        }

        static object syncLockForSearcher = new object();
        static IndexSearcher CreateOrGetIndexSearcher(string luceneIndex)
        {
            IndexSearcher indexSearcher;

            if (!IndexSearcherPool.TryGetValue(luceneIndex, out indexSearcher))
            {
                lock (syncLockForSearcher)
                {
                    if (!IndexSearcherPool.TryGetValue(luceneIndex, out indexSearcher))
                    {
                        indexSearcher = new IndexSearcher(CreateOrGetIndexReader(luceneIndex));
                        IndexSearcherPool.TryAdd(luceneIndex, indexSearcher);
                    }
                }
            }

            return indexSearcher;
        }

        static object syncLockForReader = new object();
        static IndexReader CreateOrGetIndexReader(string luceneIndex)
        {
            IndexReader indexReader;

            if (!IndexReaderPool.TryGetValue(luceneIndex, out indexReader))
            {
                lock (syncLockForReader)
                {
                    if (!IndexReaderPool.TryGetValue(luceneIndex, out indexReader))
                    {
                        indexReader = CreateOrGetIndexWriter(luceneIndex).GetReader(true);
                        IndexReaderPool.TryAdd(luceneIndex, indexReader);
                    }
                }
            }

            return indexReader;
        }

        internal static Document GetDocument(string luceneIndex, Term term)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var searcher = CreateOrGetIndexSearcher(luceneIndex);
                var result = searcher.Search(new TermQuery(term), 1);
                return result.ScoreDocs?.Length == 1 ? searcher.Doc(result.ScoreDocs[0].Doc) : null;
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        public static Document[] Search(string luceneIndex, Query query, int maxResults)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var searcher = CreateOrGetIndexSearcher(luceneIndex);
                var hits = searcher.Search(query, maxResults).ScoreDocs;
                return hits.Select(hit => searcher.Doc(hit.Doc)).ToArray();
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        public static string[] GetHints(string luceneIndex, string word, int maxResults, bool caseSensitive)
        {
            PrefixQuery query;

            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var searcher = CreateOrGetIndexSearcher(luceneIndex);

                if (caseSensitive)
                {
                    query = new PrefixQuery(new Term(nameof(CodeWord.Word), word));
                }
                else
                {
                    query = new PrefixQuery(new Term(nameof(CodeWord.WordLower), word.ToLower()));
                }

                var hits = searcher.Search(query, maxResults).ScoreDocs;
                return hits.Select(hit => searcher.Doc(hit.Doc)).Select(u => u.Get(nameof(CodeWord.Word))).ToArray();
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        public static CodeSource[] SearchCode(string luceneIndex, Query query, int maxResults)
        {
            try
            {
                readWriteLock.AcquireReaderLock(Constants.ReadWriteLockTimeOutMilliseconds);

                var searcher = CreateOrGetIndexSearcher(luceneIndex);
                var hits = searcher.Search(query, maxResults).ScoreDocs;
                return hits.Select(hit => GetCodeSourceFromDocument(searcher.Doc(hit.Doc))).ToArray();
            }
            finally
            {
                readWriteLock.ReleaseReaderLock();
            }
        }

        public static ConcurrentDictionary<string, IndexWriter> IndexWritesPool { get; } = new ConcurrentDictionary<string, IndexWriter>();
        static ConcurrentDictionary<string, IndexSearcher> IndexSearcherPool { get; } = new ConcurrentDictionary<string, IndexSearcher>();
        static ConcurrentDictionary<string, IndexReader> IndexReaderPool { get; } = new ConcurrentDictionary<string, IndexReader>();

        public static QueryParser GetQueryParser()
        {
            return new QueryParser(Constants.AppLuceneVersion, nameof(CodeSource.Content), GetAnalyzer());
        }

        static readonly ReaderWriterLock readWriteLock = new ReaderWriterLock();

        public static Analyzer GetAnalyzer()
        {
            return new CodeAnalyzer(Constants.AppLuceneVersion, true);
        }

        public static CodeSource GetCodeSourceFromDocument(Document document)
        {
            return new CodeSource
            {
                CodePK = document.Get(nameof(CodeSource.CodePK)),
                Content = document.Get(nameof(CodeSource.Content)),
                FileExtension = document.Get(nameof(CodeSource.FileExtension)),
                FileName = document.Get(nameof(CodeSource.FileName)),
                FilePath = document.Get(nameof(CodeSource.FilePath)),
                IndexDate = new DateTime(long.Parse(document.Get(nameof(CodeSource.IndexDate)))),
                LastWriteTimeUtc = new DateTime(long.Parse(document.Get(nameof(CodeSource.LastWriteTimeUtc))))
            };
        }
    }
}
