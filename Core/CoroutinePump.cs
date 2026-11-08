#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MVZ2CloudSaves;

/// <summary>
/// 手写协程泵。不能直接用 MonoBehaviour.StartCoroutine：
/// 这套 interop 程序集里它只生成了 string 重载。
/// 支持 `yield return 另一个 IEnumerator`（GitHubApi）和 Unity 的 AsyncOperation。
///
/// <para>由 <c>MainSceneController.Update</c> 与 <c>PerformanceManager.Update</c>
/// 的 Harmony postfix 驱动（见 <see cref="PatchManager"/>），不依赖自行注入的
/// MonoBehaviour。</para>
///
/// <para><b>必须记住正在等待的 AsyncOperation。</b>C# 迭代器一旦 MoveNext 过了
/// <c>yield return xxx</c>，下一次 MoveNext 就会直接执行后面的代码；所以不能在
/// 操作未完成时继续 MoveNext，否则 UnityWebRequest 会立刻以 status=0 结束。</para>
/// </summary>
internal static class CoroutinePump
{
    private sealed class State
    {
        public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        public AsyncOperation? Waiting;
    }

    private static readonly List<State> states = new List<State>();

    public static void Start(IEnumerator routine)
    {
        if (routine == null)
            return;
        var state = new State();
        state.Stack.Push(routine);
        states.Add(state);
    }

    public static void Tick()
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var state = states[i];
            try
            {
                while (state.Stack.Count > 0)
                {
                    // 先看有没有未完成的异步操作；没完成就整帧跳过，
                    // 绝不能调用 MoveNext。
                    if (state.Waiting != null)
                    {
                        bool done;
                        try { done = state.Waiting.isDone; }
                        catch (Exception) { done = true; }

                        if (!done)
                            break;
                        state.Waiting = null;
                    }

                    var routine = state.Stack.Peek();
                    if (!routine.MoveNext())
                    {
                        state.Stack.Pop();
                        continue;
                    }

                    var current = routine.Current;
                    if (current == null)
                        break; // wait one frame

                    if (current is IEnumerator nested)
                    {
                        state.Stack.Push(nested);
                        continue;
                    }

                    if (current is AsyncOperation op)
                    {
                        if (!op.isDone)
                        {
                            state.Waiting = op;
                            break;
                        }
                        continue;
                    }

                    break;
                }
            }
            catch (Exception e)
            {
                CloudSaveService.OnCoroutineFault(e);
                state.Stack.Clear();
                state.Waiting = null;
            }

            if (state.Stack.Count == 0)
                states.RemoveAt(i);
        }
    }
}
