using System.Reflection;
using System.Reflection.Emit;

namespace TfvcMigrator;

public static class CommandVerifier
{
    private static readonly AsyncLocal<Action<object?[]>?> ActiveInterceptor = new();
    private static readonly FieldInfo ActiveInterceptorField = typeof(CommandVerifier).GetField(nameof(ActiveInterceptor), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)!;

    public static async Task<object?[]> VerifyArgumentsAsync(Func<Task> invokeInterceptedDelegateAsync)
    {
        var arguments = (object?[]?)null;

        var previousValue = ActiveInterceptor.Value;
        ActiveInterceptor.Value = interceptedArguments =>
        {
            if (arguments is not null)
                throw new InvalidOperationException("The intercepted delegate was invoked more than once.");

            arguments = interceptedArguments;
        };
        try
        {
            await invokeInterceptedDelegateAsync().ConfigureAwait(false);
        }
        finally
        {
            ActiveInterceptor.Value = previousValue;
        }

        return arguments ?? throw new InvalidOperationException("The intercepted delegate was not invoked.");
    }

    public static Delegate Intercept(Delegate @delegate)
    {
        if (@delegate.Target is not null)
            throw new ArgumentException("Only static lambdas and static methods are supported.", nameof(@delegate));

        var parameters = @delegate.Method.GetParameters();

        // System.Linq.Expressions creates a method with an initial closure parameter which System.CommandLine
        // doesn't tolerate.
        var interceptingMethod = new DynamicMethod(@delegate.Method.Name, @delegate.Method.ReturnType, Array.ConvertAll(parameters, p => p.ParameterType));

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            interceptingMethod.DefineParameter(i + 1, parameter.Attributes, parameter.Name);
        }

        var generator = interceptingMethod.GetILGenerator();

        var interceptorLocal = generator.DeclareLocal(typeof(Action<object[]>));

        generator.Emit(OpCodes.Ldsfld, ActiveInterceptorField);
        generator.Emit(OpCodes.Callvirt, ActiveInterceptorField.FieldType.GetProperty(nameof(ActiveInterceptor.Value), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.GetMethod!);
        generator.Emit(OpCodes.Stloc, interceptorLocal);
        generator.Emit(OpCodes.Ldloc, interceptorLocal);

        var noInterceptionLabel = generator.DefineLabel();
        generator.Emit(OpCodes.Brfalse_S, noInterceptionLabel);

        // Call the intercepting delegate
        generator.Emit(OpCodes.Ldloc, interceptorLocal);
        generator.Emit(OpCodes.Ldc_I4, parameters.Length);
        generator.Emit(OpCodes.Newarr, typeof(object));

        for (var i = 0; i < parameters.Length; i++)
        {
            generator.Emit(OpCodes.Dup); // object[]
            generator.Emit(OpCodes.Ldc_I4, i);
            generator.Emit(OpCodes.Ldarg, i);

            if (parameters[i].ParameterType is { IsValueType: true } valueType)
                generator.Emit(OpCodes.Box, valueType);

            generator.Emit(OpCodes.Stelem_Ref);
        }

        generator.Emit(OpCodes.Callvirt, interceptorLocal.LocalType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!);

        if (@delegate.Method.ReturnType != typeof(void))
        {
            if (@delegate.Method.ReturnType == typeof(Task))
            {
                generator.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.GetMethod!);
            }
            else if (@delegate.Method.ReturnType == typeof(Task<int>))
            {
                generator.Emit(OpCodes.Ldc_I4_0);
                generator.Emit(OpCodes.Call, typeof(Task).GetMethod(nameof(Task.FromResult), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.MakeGenericMethod(typeof(int)));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        generator.Emit(OpCodes.Ret);

        // Call original method
        generator.MarkLabel(noInterceptionLabel);

        for (var i = 0; i < parameters.Length; i++)
            generator.Emit(OpCodes.Ldarg, i);

        generator.Emit(OpCodes.Call, @delegate.Method);
        generator.Emit(OpCodes.Ret);

        return interceptingMethod.CreateDelegate(@delegate.GetType());
    }
}
