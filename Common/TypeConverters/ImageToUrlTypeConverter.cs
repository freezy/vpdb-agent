﻿using System;
using ReactiveUI;
using VpdbAgent.Vpdb.Models;

namespace VpdbAgent.Common.TypeConverters
{

	public class ImageToUrlTypeConverter : IBindingTypeConverter
	{
		public int GetAffinityForObjects(Type fromType, Type toType)
		{
			if (fromType == typeof(string) && toType == typeof(Image)) return -1;
			if (fromType == typeof(Image) && toType == typeof(string)) return 100;
			return -1;
		}

		public bool TryConvert(object from, Type toType, object conversionHint, out object result)
		{
			if (toType == typeof(string)) {
				result = ((Image)from).Url;
				return true;
			} else {
				result = null;
				return false;
			}
		}
	}
}