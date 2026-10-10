using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace StoreAndCraft
{
    /// <summary>
    /// Calls a private vanilla method without parameters (GetFuel, GetQueueSize, HaveDoneItem, GetHoneyLevel ...)
    /// through a delegate compiled once per method, instead of MethodInfo.Invoke on every call (reflection, a boxed
    /// result and garbage each time: these run every second for every station). Same result as the Invoke path;
    /// when a delegate cannot be built the Invoke path is used. Exceptions of the method itself still come through.
    /// </summary>
    internal static class FastCall
    {
        private static readonly Dictionary<MethodInfo, Func<object, bool>> Bools = new Dictionary<MethodInfo, Func<object, bool>>();
        private static readonly Dictionary<MethodInfo, Func<object, int>> Ints = new Dictionary<MethodInfo, Func<object, int>>();
        private static readonly Dictionary<MethodInfo, Func<object, float>> Floats = new Dictionary<MethodInfo, Func<object, float>>();
        private static readonly Dictionary<MethodInfo, Func<object, double>> Doubles = new Dictionary<MethodInfo, Func<object, double>>();
        private static readonly Dictionary<MethodInfo, Func<object, object>> Objects = new Dictionary<MethodInfo, Func<object, object>>();

        public static bool Bool(MethodInfo method, object target)
        {
            Func<object, bool> f = Get(Bools, method);
            if (f != null)
                return f(target);
            object v = method.Invoke(target, null);
            return v is bool && (bool)v;
        }

        /// <summary>int or enum result as int (Convert.ToInt32 of the Invoke path).</summary>
        public static int Int(MethodInfo method, object target)
        {
            Func<object, int> f = Get(Ints, method);
            if (f != null)
                return f(target);
            return Convert.ToInt32(method.Invoke(target, null));
        }

        public static float Float(MethodInfo method, object target)
        {
            Func<object, float> f = Get(Floats, method);
            if (f != null)
                return f(target);
            return Convert.ToSingle(method.Invoke(target, null));
        }

        public static double Double(MethodInfo method, object target)
        {
            Func<object, double> f = Get(Doubles, method);
            if (f != null)
                return f(target);
            return Convert.ToDouble(method.Invoke(target, null));
        }

        public static object Object(MethodInfo method, object target)
        {
            Func<object, object> f = Get(Objects, method);
            if (f != null)
                return f(target);
            return method.Invoke(target, null);
        }

        private static Func<object, R> Get<R>(Dictionary<MethodInfo, Func<object, R>> cache, MethodInfo method)
        {
            Func<object, R> f;
            if (cache.TryGetValue(method, out f))
                return f;
            f = Build<R>(method);
            cache[method] = f; // null = not buildable: Invoke path from now on
            return f;
        }

        private static Func<object, R> Build<R>(MethodInfo method)
        {
            try
            {
                if (method.GetParameters().Length != 0 || method.ReturnType == typeof(void))
                    return null;
                ParameterExpression target = Expression.Parameter(typeof(object), "target");
                Expression call = method.IsStatic
                    ? Expression.Call(method)
                    : Expression.Call(Expression.Convert(target, method.DeclaringType), method);
                Expression result = method.ReturnType == typeof(R) ? call : Expression.Convert(call, typeof(R));
                return Expression.Lambda<Func<object, R>>(result, target).Compile();
            }
            catch
            {
                return null;
            }
        }
    }
}
