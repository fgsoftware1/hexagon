﻿using System;
using System.Collections.Generic;
using FMOD;
using UnityEngine;

namespace FMODUnity
{
    public class EditorBankRef : ScriptableObject
    {
        [SerializeField] public string Path;

        [SerializeField] public string Name;

        [SerializeField] public string StudioPath;

        [SerializeField] private long lastModified;

        [SerializeField] public RESULT LoadResult;

        [SerializeField] public List<NameValuePair> FileSizes;

        public bool Exists;

        public DateTime LastModified
        {
            get => new DateTime(lastModified);
            set => lastModified = value.Ticks;
        }

        public static string CalculateName(string filePath, string basePath)
        {
            var relativePath = filePath.Substring(basePath.Length + 1);
            var extension = System.IO.Path.GetExtension(relativePath);

            var name = relativePath.Substring(0, relativePath.Length - extension.Length);
            name = RuntimeUtils.GetCommonPlatformPath(name);

            return name;
        }

        public void SetPath(string filePath, string basePath)
        {
            Path = RuntimeUtils.GetCommonPlatformPath(filePath);
            Name = CalculateName(filePath, basePath);
            name = "bank:/" + Name + System.IO.Path.GetExtension(filePath);
        }

        public void SetStudioPath(string studioPath)
        {
            string stringCmp;
            stringCmp = System.IO.Path.GetFileName(Name);
            if (!studioPath.Contains(stringCmp))
            {
                // No match means localization
                studioPath = studioPath.Substring(0, studioPath.LastIndexOf("/") + 1);
                studioPath += stringCmp;
            }

            StudioPath = studioPath;
        }

        [Serializable]
        public class NameValuePair
        {
            public string Name;
            public long Value;

            public NameValuePair(string name, long value)
            {
                Name = name;
                Value = value;
            }
        }
    }
}