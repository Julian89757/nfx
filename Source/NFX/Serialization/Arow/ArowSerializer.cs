﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using NFX.IO;
using NFX.DataAccess.CRUD;

namespace NFX.Serialization.Arow
{
  /// <summary>
  /// Designates classes that register their single instance via a call to ArowSerializer.Register().
  /// These classes are generated by cl arow compiler
  /// </summary>
  public interface ITypeSerializationCore
  {
    void Register();
    void Serialize(TypedRow row, WritingStreamer streamer);
    void Deserialize(TypedRow row, ReadingStreamer streamer);
  }

  /// <summary>
  /// Facade for performing Arow serilalization.
  /// Arow format is purposely designed for "[a]daptable [row]"/version tolerant serialization that eschews creating extra copies and
  /// object instances. The serializer is used in conjunction with cl compiler that generates type-specific static serializer cores
  /// for every type that supports the format
  /// </summary>
  public static class ArowSerializer
  {
    public const string AROW_TARGET = "AROW-SERIALIZER";

    private static object s_Lock = new object();
    private static volatile Dictionary<Type, ITypeSerializationCore> s_Serializers = new Dictionary<Type, ITypeSerializationCore>();


    public static void RegisterTypeSerializationCores(Assembly asm)
    {
      foreach(var t in asm.GetTypes().Where( t => t.IsClass && !t.IsAbstract && typeof(ITypeSerializationCore).IsAssignableFrom(t)))
      {
        var core = Activator.CreateInstance(t) as ITypeSerializationCore;
        core.Register();
      }
    }


    public static void Serialize(TypedRow row, WritingStreamer streamer, bool header = true)
    {
      ITypeSerializationCore core;
      var tRow = row.GetType();
      if (!s_Serializers.TryGetValue(tRow, out core))
        throw new ArowException(StringConsts.AROW_TYPE_NOT_SUPPORTED_ERROR.Args(tRow.FullName));

      var ar = row as IAmorphousData;
      if (ar!=null)
      {
        if (ar.AmorphousDataEnabled) ar.BeforeSave(AROW_TARGET);
      }

      //1 Header
      if (header) Writer.WriteHeader(streamer);

          //2 Body
          core.Serialize(row, streamer);

      //3 EORow
      Writer.WriteEORow(streamer);
    }


    public static void Deserialize(TypedRow row, ReadingStreamer streamer, bool header = true)
    {
      var ok = TryDeserialize(row, streamer, header);
      if (!ok)
        throw new ArowException(StringConsts.AROW_TYPE_NOT_SUPPORTED_ERROR.Args(row.GetType().FullName));
    }

    public static bool TryDeserialize(TypedRow row, ReadingStreamer streamer, bool header = true)
    {
      ITypeSerializationCore core;
      var tRow = row.GetType();
      if (!s_Serializers.TryGetValue(tRow, out core))
        return false;

      //1 Header
      if (header) Reader.ReadHeader(streamer);

      //2 Body
      core.Deserialize(row, streamer);

      var ar = row as IAmorphousData;
      if (ar!=null)
      {
        if (ar.AmorphousDataEnabled) ar.AfterLoad(AROW_TARGET);
      }

      return true;
    }

    public static bool IsRowTypeSupported(Type tRow)
    {
      return s_Serializers.ContainsKey(tRow);
    }

    /// <summary>
    /// Registers ITypeSerializationCore so it can be used globaly to serialize TypedRows in Arow format
    /// </summary>
    public static bool Register(Type tRow, ITypeSerializationCore core)
    {
      lock(s_Lock)
      {
        if (s_Serializers.ContainsKey(tRow)) return false;
        var dict = new Dictionary<Type, ITypeSerializationCore>(s_Serializers);
        dict.Add(tRow, core);
        s_Serializers = dict;//atomic
        return true;
      }
    }
  }
}
