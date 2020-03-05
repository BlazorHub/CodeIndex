﻿using System;
using System.IO;
using CodeIndex.IndexBuilder;
using CodeIndex.Search;
using NUnit.Framework;

namespace CodeIndex.Test
{
    public class BaseTest
    {
        protected const string IndexDirName = "CodeIndex";
        protected string TempDir { get; set; }
        protected string TempIndexDir => Path.Combine(TempDir, IndexDirName);

        QueryGenerator generator;
        protected QueryGenerator Generator => generator ?? (generator = new QueryGenerator());

        [SetUp]
        protected virtual void SetUp()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "CodeIndex.Test_" + Guid.NewGuid());

            var dir = new DirectoryInfo(TempDir);
            if (!dir.Exists)
            {
                dir.Create();
            }
        }

        [TearDown]
        protected virtual void TearDown()
        {
            LucenePool.SaveResultsAndClearLucenePool(TempIndexDir);
            DeleteAllFilesInTempDir(TempDir);
        }

        void DeleteAllFilesInTempDir(string srcPath)
        {
            var dir = new DirectoryInfo(srcPath);
            dir.Delete(true);
        }
    }
}
