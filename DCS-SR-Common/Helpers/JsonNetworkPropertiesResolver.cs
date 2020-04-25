using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Serialization;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers
{
    public class JsonNetworkPropertiesResolver : DefaultContractResolver
    {
        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            List<MemberInfo> list = base.GetSerializableMembers(objectType);

            list = list.Where(pi => !Attribute.IsDefined(pi, typeof(JsonNetworkIgnoreSerializationAttribute))).ToList();

            return list;
            //Return properties that do NOT have the JsonIgnoreSerializationAttribute
            // return objectType.GetProperties()
            //     .Where(pi => !Attribute.IsDefined(pi, typeof(JsonIgnoreSerializationAttribute)))
            //     .ToList<MemberInfo>();
        }
    }
}
