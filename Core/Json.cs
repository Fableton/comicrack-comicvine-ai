using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>Small helpers over the untyped result of JavaScriptSerializer.</summary>
	internal static class Json
	{
		private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

		public static Dictionary<string, object> ParseObject(string json) => serializer.DeserializeObject(json) as Dictionary<string, object>;

		/// <summary>The array (object[]) or object of a JSON text.</summary>
		public static object ParseArrayText(string json) => serializer.DeserializeObject(json);

		public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;

		public static IEnumerable<object> Arr(object o)
		{
			if (o is IEnumerable e && !(o is string) && !(o is IDictionary))
			{
				foreach (object item in e)
				{
					yield return item;
				}
			}
		}

		public static object Get(Dictionary<string, object> d, string key)
		{
			if (d != null && d.TryGetValue(key, out object value))
			{
				return value;
			}
			return null;
		}

		public static string Str(Dictionary<string, object> d, string key) => Convert.ToString(Get(d, key), CultureInfo.InvariantCulture) ?? "";

		public static int Int(Dictionary<string, object> d, string key)
		{
			object value = Get(d, key);
			if (value == null)
			{
				return 0;
			}
			return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : 0;
		}

		public static string Serialize(object o) => serializer.Serialize(o);
	}
}
