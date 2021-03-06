﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sightstone.Helper
{
    public static class Classes
    {
        public static List<T> GetInstances<T>()
        {
            return (from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.BaseType == (typeof(T)) && t.GetConstructor(Type.EmptyTypes) != null
                    select (T)Activator.CreateInstance(t)).ToList();
        }
    }
}
