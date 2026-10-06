using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    [Serializable]
    public class RegistryData
    {
        public int schema = 1;
        public List<VehicleRecord> vehicles = new List<VehicleRecord>();
    }

    public enum RegistryLoadOutcome
    {
        /// <summary>No file yet (first run): starts empty.</summary>
        Fresh,
        Loaded,
        /// <summary>The main file was unreadable; the backup copy was used.</summary>
        RecoveredFromBackup,
        /// <summary>Both files were unreadable: started empty (the unreadable file is kept as *.corrupt).</summary>
        StartedEmptyAfterCorruption,
        /// <summary>The file was written by a newer version of the app. It is read but never overwritten.</summary>
        NewerVersionReadOnly,
    }

    /// <summary>
    /// The single local record of every downloaded vehicle: what files it uses, which version, and when.
    /// Stored as one JSON file. Writes are atomic (temp file then swap) and the previous copy is kept
    /// as a backup, so a crash or power loss mid-save cannot leave the registry half-written.
    /// </summary>
    public class VehicleRegistry
    {
        public const int CurrentSchema = 1;

        private readonly string _path;
        private RegistryData _data = new RegistryData();

        public VehicleRegistry(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath");
            _path = filePath;
        }

        public bool ReadOnly { get; private set; }
        public string LastSaveError { get; private set; }
        public RegistryLoadOutcome LoadOutcome { get; private set; }

        private string BackupPath { get { return _path + ".bak"; } }
        private string TempPath { get { return _path + ".tmp"; } }

        public IList<VehicleRecord> Vehicles { get { return new List<VehicleRecord>(_data.vehicles); } }
        public int Count { get { return _data.vehicles.Count; } }

        public VehicleRecord Get(string vehicleId)
        {
            int i = IndexOf(vehicleId);
            return i < 0 ? null : _data.vehicles[i];
        }

        public RegistryLoadOutcome Load()
        {
            ReadOnly = false;
            LastSaveError = null;

            bool mainExists = File.Exists(_path);
            bool bakExists = File.Exists(BackupPath);
            if (!mainExists && !bakExists)
            {
                _data = new RegistryData();
                return LoadOutcome = RegistryLoadOutcome.Fresh;
            }

            RegistryData parsed;
            if (mainExists && TryRead(_path, out parsed))
            {
                _data = parsed;
                return LoadOutcome = FinishLoad(RegistryLoadOutcome.Loaded);
            }

            // Main file missing or unreadable: keep a copy of the bad file for inspection, try the backup.
            if (mainExists) KeepCorruptCopy();
            if (bakExists && TryRead(BackupPath, out parsed))
            {
                _data = parsed;
                return LoadOutcome = FinishLoad(RegistryLoadOutcome.RecoveredFromBackup);
            }

            _data = new RegistryData();
            return LoadOutcome = RegistryLoadOutcome.StartedEmptyAfterCorruption;
        }

        public bool Upsert(VehicleRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.vehicleId)) return false;
            if (record.bundles == null) record.bundles = new List<BundleRef>();
            record.vehicleId = record.vehicleId.Trim();
            int i = IndexOf(record.vehicleId);
            if (i >= 0) _data.vehicles[i] = record; else _data.vehicles.Add(record);
            return Save();
        }

        public bool Remove(string vehicleId)
        {
            int i = IndexOf(vehicleId);
            if (i < 0) return false;
            _data.vehicles.RemoveAt(i);
            return Save();
        }

        public bool Clear()
        {
            _data.vehicles.Clear();
            return Save();
        }

        public bool Touch(string vehicleId, DateTime utcNow)
        {
            var r = Get(vehicleId);
            if (r == null) return false;
            r.lastOpenedUtcTicks = utcNow.ToUniversalTime().Ticks;
            return Save();
        }

        public bool Save()
        {
            if (ReadOnly)
            {
                LastSaveError = "The vehicle list was written by a newer version of the app and is read-only here.";
                return false;
            }
            try
            {
                _data.schema = CurrentSchema;
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(TempPath, JsonUtility.ToJson(_data, true));
                if (File.Exists(_path))
                    File.Replace(TempPath, _path, BackupPath, true);   // atomic swap, previous copy kept as .bak
                else
                    File.Move(TempPath, _path);

                LastSaveError = null;
                return true;
            }
            catch (Exception e)
            {
                LastSaveError = e.Message;
                try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { }
                return false;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private RegistryLoadOutcome FinishLoad(RegistryLoadOutcome ok)
        {
            if (_data.schema > CurrentSchema)
            {
                ReadOnly = true;
                return RegistryLoadOutcome.NewerVersionReadOnly;
            }
            // Repair anything a hand-edited or older file could contain
            _data.vehicles.RemoveAll(v => v == null || string.IsNullOrWhiteSpace(v.vehicleId));
            foreach (var v in _data.vehicles)
                if (v.bundles == null) v.bundles = new List<BundleRef>();
            return ok;
        }

        private static bool TryRead(string path, out RegistryData data)
        {
            data = null;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;
                var parsed = JsonUtility.FromJson<RegistryData>(json);
                if (parsed == null || parsed.vehicles == null) return false;
                data = parsed;
                return true;
            }
            catch { return false; }
        }

        private void KeepCorruptCopy()
        {
            try { File.Copy(_path, _path + ".corrupt", true); } catch { }
        }

        private int IndexOf(string vehicleId)
        {
            string id = (vehicleId ?? "").Trim();
            for (int i = 0; i < _data.vehicles.Count; i++)
                if (string.Equals(_data.vehicles[i].vehicleId, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
