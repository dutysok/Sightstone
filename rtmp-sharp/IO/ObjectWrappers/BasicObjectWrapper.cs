﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RtmpSharp.IO.AMF3;

namespace RtmpSharp.IO.ObjectWrappers
{
    internal class BasicObjectWrapper : IObjectWrapper
    {
        // TODO: Turn this into a real cache... which expires old items.
        private readonly Dictionary<Type, ClassDescription> cache = new Dictionary<Type, ClassDescription>();
        private readonly SerializationContext serializationContext;

        public BasicObjectWrapper(SerializationContext serializationContext)
        {
            this.serializationContext = serializationContext;
        }

        public bool GetIsExternalizable(object instance)
        {
            return instance is IExternalizable;
        }

        public bool GetIsDynamic(object instance)
        {
            return instance is AsObject;
        }

        // Gets the class definition for an object `obj`, applying transformations like type name mappings
        public virtual ClassDescription GetClassDescription(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");
            var type = obj.GetType();

            ClassDescription cachedDescription;
            if (cache.TryGetValue(type, out cachedDescription))
                return cachedDescription;

            var serializable = Helper.GetSerializableFields(type);
            var classMembers = new List<BasicMemberWrapper>();

            // add fields and properties
            classMembers.AddRange(serializable.Fields.Select(fieldInfo => new BasicMemberWrapper(fieldInfo)));
            foreach (var propertyInfo in serializable.Properties)
            {
                // There is no reflection API that allows us to check whether a variable hides
                // another variable (for example, with the `new` keyword). We need to access the
                // property by name and catch an ambiguous match.
                //
                // Currently, the logic following that which only allows variables from the
                // declaring type forward doesn't work in all cases work in all cases. Assume we
                // have an inheritence hierarchy of `operating-system -> linux -> arch-linux`. In
                // this case, if both `operating-system` and `linux` declare a `Name` field that is
                // not inherited but `arch-linux` does not then we expect the `Name` field from
                // `linux` to be serialized, but as it is both `Name` fields from `operating-system`
                // and `linux` are ignored.
                //
                //
                // In practice it does not matter for my current use cases, but this may trip up
                // some people if I ever open source this
                try
                {
                    type.GetProperty(propertyInfo.Name);
                }
                catch (AmbiguousMatchException)
                {
                    if (type.DeclaringType != type)
                        continue;
                }

                var classMember = new BasicMemberWrapper(propertyInfo);
                classMembers.Add(classMember);
            }

            var typeName = serializationContext.GetAlias(type.FullName);
            return new BasicObjectClassDescription(typeName, classMembers.Cast<IMemberWrapper>().ToArray(),
                GetIsExternalizable(obj), GetIsDynamic(obj));
        }

        private class BasicObjectClassDescription : ClassDescription
        {
            internal BasicObjectClassDescription(string name, IMemberWrapper[] members, bool externalizable,
                bool dynamic)
                : base(name, members, externalizable, dynamic)
            {
                MemberLookup = members
                    .Select(
                        x =>
                            x.SerializedName == null
                                ? new {x.Name, Member = x}
                                : new {Name = x.SerializedName, Member = x})
                    .ToLookup(x => x.Name)
                    .ToDictionary(x => x.Key, x => x.First().Member);
            }

            // Because we are cached by the `BasicObjectWrapper`, speed up lookups so that read deserialisation is (slightly) faster.
            public Dictionary<string, IMemberWrapper> MemberLookup { get; }

            public override bool TryGetMember(string name, out IMemberWrapper memberWrapper)
            {
                return MemberLookup.TryGetValue(name, out memberWrapper);
            }
        }

        private class BasicMemberWrapper : IMemberWrapper
        {
            private readonly FieldInfo fieldInfo;
            private readonly bool isField;
            private readonly PropertyInfo propertyInfo;

            public BasicMemberWrapper(PropertyInfo propertyInfo)
            {
                Name = propertyInfo.Name;
                this.propertyInfo = propertyInfo;
                isField = false;
                LoadSerializedName(propertyInfo);
            }

            public BasicMemberWrapper(FieldInfo fieldInfo)
            {
                Name = fieldInfo.Name;
                this.fieldInfo = fieldInfo;
                isField = true;
                LoadSerializedName(fieldInfo);
            }

            public string Name { get; }
            public string SerializedName { get; private set; }

            public object GetValue(object instance)
            {
                return isField ? fieldInfo.GetValue(instance) : propertyInfo.GetValue(instance);
            }

            public void SetValue(object instance, object value)
            {
                var targetType = isField ? fieldInfo.FieldType : propertyInfo.PropertyType;
                var val = MiniTypeConverter.ConvertTo(value, targetType);
                if (isField)
                    fieldInfo.SetValue(instance, val);
                else
                    propertyInfo.SetValue(instance, val);
            }

            private void LoadSerializedName(MemberInfo memberInfo)
            {
                var attribute = memberInfo.GetCustomAttribute<SerializedNameAttribute>(true);
                SerializedName = attribute != null ? attribute.SerializedName : Name;
            }
        }

        private static class Helper
        {
            public static FieldsAndProperties GetSerializableFields(object obj)
            {
                return GetSerializableFields(obj.GetType());
            }

            public static FieldsAndProperties GetSerializableFields(Type type)
            {
                var filteredProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => x.GetCustomAttributes(typeof (TransientAttribute), true).Length == 0)
                    .Where(x => x.GetGetMethod() != null)
                    // skip if access modifier makes it inaccessible (when GetGetMethod() returns null)
                    .Where(x => x.GetGetMethod().GetParameters().Length == 0);
                    // skip if not a "pure" get property has parameters (eg get property for `class[int index]`)

                var filteredFieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => x.GetCustomAttributes(typeof (NonSerializedAttribute), true).Length == 0)
                    .Where(x => x.GetCustomAttributes(typeof (TransientAttribute), true).Length == 0);

                return new FieldsAndProperties
                {
                    Properties = filteredProperties.ToArray(),
                    Fields = filteredFieldInfos.ToArray()
                };
            }

            public struct FieldsAndProperties
            {
                public FieldInfo[] Fields;
                public PropertyInfo[] Properties;
            }
        }
    }
}