﻿using libsignal;
using libsignal.ecc;
using libsignal.state;
using libsignalservice.push;
using libsignalservice.util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace Signal_Windows.Storage
{
    public class Store : SignalProtocolStore
    {
        [JsonIgnore] public static Store Instance;
        [JsonIgnore] public static string localFolder = ApplicationData.Current.LocalFolder.Path;
        [JsonIgnore] public static ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

        [JsonIgnore]
        public static JsonConverter[] Converters = new JsonConverter[] {
                                                new IdentityKeyPairConverter(),
                                                new IdentityKeyConverter(),
                                                new ByteArrayConverter()};

        public uint deviceId { get; set; } = SignalServiceAddress.DEFAULT_DEVICE_ID;
        public String username { get; set; }
        public String password { get; set; }
        public String signalingKey { get; set; }
        public uint preKeyIdOffset { get; set; }
        public uint nextSignedPreKeyId { get; set; }
        public bool registered { get; set; } = false;
        public JsonPreKeyStore jsonPreKeyStore { get; set; }
        public JsonIdentityKeyStore jsonIdentityKeyStore { get; set; }
        public JsonSessionStore jsonSessionStore { get; set; }
        public JsonSignedPreKeyStore jsonSignedPreKeyStore { get; set; }

        public Store()
        {
            Instance = this;
        }

        public Store(IdentityKeyPair identityKey, uint registrationId)
        {
            Instance = this;
            jsonPreKeyStore = new JsonPreKeyStore();
            jsonSessionStore = new JsonSessionStore();
            jsonSignedPreKeyStore = new JsonSignedPreKeyStore();
            jsonIdentityKeyStore = new JsonIdentityKeyStore(identityKey, registrationId);
        }

        public void Save()
        {
            try
            {
                using (FileStream fs = File.Open(localFolder + @"\" + LocalSettings.Values["Username"] + "Store.json", FileMode.Truncate))
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    string s = JsonConvert.SerializeObject(this, Formatting.Indented, Converters);
                    sw.Write(s);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("SignalProtocolStore failed to save!");
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.StackTrace);
            }
        }

        public IdentityKeyPair GetIdentityKeyPair()
        {
            return jsonIdentityKeyStore.GetIdentityKeyPair();
        }

        public uint GetLocalRegistrationId()
        {
            return jsonIdentityKeyStore.GetLocalRegistrationId();
        }

        public bool SaveIdentity(SignalProtocolAddress address, IdentityKey identityKey)
        {
            jsonIdentityKeyStore.SaveIdentity(address, identityKey);
            Save();
            return true;
        }

        public bool IsTrustedIdentity(SignalProtocolAddress address, IdentityKey identityKey)
        {
            return jsonIdentityKeyStore.IsTrustedIdentity(address, identityKey);
        }

        public PreKeyRecord LoadPreKey(uint preKeyId)
        {
            return jsonPreKeyStore.LoadPreKey(preKeyId);
        }

        public void StorePreKey(uint preKeyId, PreKeyRecord record)
        {
            Debug.WriteLine(String.Format("storing prekey {0} {1}", preKeyId, record));
            jsonPreKeyStore.StorePreKey(preKeyId, record);
            Save();
        }

        public bool ContainsPreKey(uint preKeyId)
        {
            return jsonPreKeyStore.ContainsPreKey(preKeyId);
        }

        public void RemovePreKey(uint preKeyId)
        {
            jsonPreKeyStore.RemovePreKey(preKeyId);
            Save();
        }

        public SessionRecord LoadSession(SignalProtocolAddress address)
        {
            return jsonSessionStore.LoadSession(address);
        }

        public List<uint> GetSubDeviceSessions(string name)
        {
            return jsonSessionStore.GetSubDeviceSessions(name);
        }

        public void StoreSession(SignalProtocolAddress address, SessionRecord record)
        {
            jsonSessionStore.StoreSession(address, record);
            Save();
        }

        public bool ContainsSession(SignalProtocolAddress address)
        {
            return jsonSessionStore.ContainsSession(address);
        }

        public void DeleteSession(SignalProtocolAddress address)
        {
            jsonSessionStore.DeleteSession(address);
            Save();
        }

        public void DeleteAllSessions(string name)
        {
            jsonSessionStore.DeleteAllSessions(name);
            Save();
        }

        public SignedPreKeyRecord LoadSignedPreKey(uint signedPreKeyId)
        {
            return jsonSignedPreKeyStore.LoadSignedPreKey(signedPreKeyId);
        }

        public List<SignedPreKeyRecord> LoadSignedPreKeys()
        {
            return jsonSignedPreKeyStore.LoadSignedPreKeys();
        }

        public void StoreSignedPreKey(uint signedPreKeyId, SignedPreKeyRecord record)
        {
            jsonSignedPreKeyStore.StoreSignedPreKey(signedPreKeyId, record);
            Save();
        }

        public bool ContainsSignedPreKey(uint signedPreKeyId)
        {
            return jsonSignedPreKeyStore.ContainsSignedPreKey(signedPreKeyId);
        }

        public void RemoveSignedPreKey(uint signedPreKeyId)
        {
            jsonSignedPreKeyStore.RemoveSignedPreKey(signedPreKeyId);
            Save();
        }
    }

    public class JsonPreKeyStore : PreKeyStore
    {
        [JsonProperty] private Dictionary<uint, byte[]> store = new Dictionary<uint, byte[]>();

        public bool ContainsPreKey(uint preKeyId)
        {
            return store.ContainsKey(preKeyId);
        }

        public PreKeyRecord LoadPreKey(uint preKeyId)
        {
            if (store.ContainsKey(preKeyId))
            {
                return new PreKeyRecord(store[preKeyId]);
            }
            throw new InvalidKeyException("no such PreKeyRecord");
        }

        public void RemovePreKey(uint preKeyId)
        {
            store.Remove(preKeyId);
        }

        public void StorePreKey(uint preKeyId, PreKeyRecord record)
        {
            store[preKeyId] = record.serialize();
            Store.Instance.Save();
        }
    }

    public class JsonIdentityKeyStore : IdentityKeyStore
    {
        [JsonProperty] private IdentityKeyPair identityKeyPair { get; set; }
        [JsonProperty] private uint registrationId { get; set; }
        [JsonProperty] private Dictionary<string, List<IdentityKey>> trustedKeys { get; set; } = new Dictionary<string, List<IdentityKey>>();

        public JsonIdentityKeyStore(IdentityKeyPair identityKey, uint registrationId)
        {
            this.identityKeyPair = identityKey;
            this.registrationId = registrationId;
        }

        public IdentityKeyPair GetIdentityKeyPair()
        {
            return identityKeyPair;
        }

        public uint GetLocalRegistrationId()
        {
            return registrationId;
        }

        public bool IsTrustedIdentity(SignalProtocolAddress address, IdentityKey identityKey)
        {
            if (!trustedKeys.ContainsKey(address.Name))
            {
                return true;
            }

            List<IdentityKey> identities = trustedKeys[address.Name];
            foreach (var identity in identities)
            {
                if (identity.Equals(identityKey))
                {
                    return true;
                }
            }
            return false;
        }

        public bool SaveIdentity(SignalProtocolAddress address, IdentityKey identityKey) //TODO why bool
        {
            if (!trustedKeys.ContainsKey(address.Name))
            {
                trustedKeys[address.Name] = new List<IdentityKey>();
            }
            trustedKeys[address.Name].Add(identityKey);
            Store.Instance.Save();
            return true;
        }
    }

    public class JsonSessionStore : SessionStore
    {
        [JsonProperty] private Dictionary<string, Dictionary<uint, byte[]>> sessions = new Dictionary<string, Dictionary<uint, byte[]>>();

        public bool ContainsSession(SignalProtocolAddress address)
        {
            if (sessions.ContainsKey(address.Name))
            {
                if (sessions[address.Name].ContainsKey(address.DeviceId))
                {
                    return true;
                }
            }
            return false;
        }

        public void DeleteAllSessions(string name)
        {
            sessions.Remove(name);
            Store.Instance.Save();
        }

        public void DeleteSession(SignalProtocolAddress address)
        {
            sessions[address.Name].Remove(address.DeviceId);
            Store.Instance.Save();
        }

        public List<uint> GetSubDeviceSessions(string name)
        {
            List<uint> deviceIds = new List<uint>();
            foreach (var session in sessions[name])
            {
                if (session.Key != SignalServiceAddress.DEFAULT_DEVICE_ID)
                {
                    deviceIds.Add(session.Key);
                }
            }
            return deviceIds;
        }

        public SessionRecord LoadSession(SignalProtocolAddress address)
        {
            if (ContainsSession(address))
                return new SessionRecord(sessions[address.Name][address.DeviceId]);
            else
                return new SessionRecord();
        }

        public void StoreSession(SignalProtocolAddress address, SessionRecord record)
        {
            if (!sessions.ContainsKey(address.Name))
            {
                sessions[address.Name] = new Dictionary<uint, byte[]>();
            }
            sessions[address.Name][address.DeviceId] = record.serialize();
            Store.Instance.Save();
        }
    }

    public class JsonSignedPreKeyStore : SignedPreKeyStore
    {
        [JsonProperty] private Dictionary<uint, byte[]> store = new Dictionary<uint, byte[]>();

        public bool ContainsSignedPreKey(uint signedPreKeyId)
        {
            return store.ContainsKey(signedPreKeyId);
        }

        public SignedPreKeyRecord LoadSignedPreKey(uint signedPreKeyId)
        {
            if (store.ContainsKey(signedPreKeyId))
            {
                return new SignedPreKeyRecord(store[signedPreKeyId]);
            }
            throw new InvalidKeyException();
        }

        public List<SignedPreKeyRecord> LoadSignedPreKeys()
        {
            List<SignedPreKeyRecord> preKeys = new List<SignedPreKeyRecord>();
            foreach (var key in store.Keys)
            {
                preKeys.Add(new SignedPreKeyRecord(store[key]));
            }
            return preKeys;
        }

        public void RemoveSignedPreKey(uint signedPreKeyId)
        {
            store.Remove(signedPreKeyId);
            Store.Instance.Save();
        }

        public void StoreSignedPreKey(uint signedPreKeyId, SignedPreKeyRecord record)
        {
            store[signedPreKeyId] = record.serialize();
            Store.Instance.Save();
        }
    }

    public class ByteArrayConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(byte[]))
            {
                return true;
            }
            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string json = (string)reader.Value;
            return Base64.decode(json);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.GetType() == typeof(byte[]))
            {
                byte[] arr = (byte[])value;
                writer.WriteValue(Base64.encodeBytes(arr));
            }
            else
            {
                throw new ArgumentException();
            }
        }
    }

    public class IdentityKeyConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(IdentityKey))
            {
                return true;
            }
            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string json = (string)reader.Value;
            return new IdentityKey(Curve.decodePoint(Base64.decodeWithoutPadding(json), 0));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.GetType() == typeof(IdentityKey))
            {
                IdentityKey ik = (IdentityKey)value;
                writer.WriteValue(Base64.encodeBytes(ik.serialize()));
            }
            else
            {
                throw new ArgumentException();
            }
        }
    }

    public class IdentityKeyPairConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(IdentityKeyPair))
            {
                return true;
            }
            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string json = (string)reader.Value;
            return new IdentityKeyPair(Base64.decode(json));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.GetType() == typeof(IdentityKeyPair))
            {
                IdentityKeyPair ik = (IdentityKeyPair)value;
                writer.WriteValue(Base64.encodeBytes(ik.serialize()));
            }
            else
            {
                throw new ArgumentException();
            }
        }
    }
}