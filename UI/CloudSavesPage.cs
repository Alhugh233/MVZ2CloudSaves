#nullable enable

using System;
using System.Text;
using DSHCore.Ui;
using Il2CppInterop.Runtime;
using MVZ2.Managers;
using TMPro;
using UnityEngine;

namespace MVZ2CloudSaves;

internal sealed class CloudSavesPage : AddonPage
{
    private static CloudSavesPage? instance;

    private UiList? list;
    private TMP_InputField? tokenInput;
    private TMP_InputField? existingInput;
    private TMP_InputField? createInput;
    private string localMessage = string.Empty;

    public override string Id => "CloudSaves";
    public override string EntryLabel => Loc.T("云存档");
    public override string Title => Loc.T("云存档");

    public CloudSavesPage()
    {
        instance = this;
        Register();
    }

    public static void NotifyChanged()
    {
        try
        {
            if (instance != null && instance.IsOpen)
                instance.Rebuild();
        }
        catch
        {
        }
    }

    protected override bool BuildContent(AddonShell shell)
    {
        var node = shell.Node("Center/List");
        if (node == null)
            return Fail("页面壳里找不到 Center/List。");

        shell.PreparePanel(node, clearChildren: false, spacing: 8f, padding: 10);

        list = UiList.Create(node.transform, "CloudSavesList");
        if (list == null || !list.IsValid)
            return Fail("可滚动列表建不出来。");
        list.SetFlexible();
        return true;
    }

    protected override void OnOpened()
    {
        Rebuild();
    }

    protected override void OnBuildFailed(string error)
    {
        Diag.Warn("云存档页面建不出来：" + error);
    }

    private void Rebuild()
    {
        if (list == null)
            return;
        try
        {
            // 后台事件（保存、拉取完成等）也会触发 Rebuild。重建前保留用户
            // 已经输入但还没提交的内容，否则输入框会被“清空”。
            var tokenDraft = tokenInput != null ? tokenInput.text : string.Empty;
            var repoDraft = existingInput != null ? existingInput.text : string.Empty;
            var createDraft = createInput != null ? createInput.text : string.Empty;

            list.Clear();
            tokenInput = null;
            existingInput = null;
            createInput = null;

            AddParagraph(CloudSaveService.Status);
            if (!string.IsNullOrEmpty(localMessage))
                AddParagraph(localMessage);
            AddParagraph(Loc.T("仓库") + "：" + (CloudSavesConfig.IsConfigured
                ? CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value
                : Loc.T("未配置")));
            AddParagraph(Loc.T("Token") + "：" + Loc.T(TokenStore.Describe()));

            if (!string.IsNullOrEmpty(CloudSaveService.Conflict))
            {
                AddParagraph(Loc.T("冲突") + "：" + CloudSaveService.Conflict);
                AddButtonRow("上传本地覆盖云端", new Action(ConfirmForceUpload),
                    "从云端覆盖本地", new Action(ConfirmForceDownload));
            }

            AddSection("GitHub Token");
            AddInputRow("Token", "", tokenDraft, true, "保存并验证", SaveToken);
            if (tokenDraft.Length > 0)
                AddParagraph(Loc.T("Token 已输入 {0} 个字符", tokenDraft.Length));
            AddButtonRow("从剪贴板粘贴", new Action(PasteToken),
                "清除 Token", new Action(ClearToken),
                "验证仓库", new Action(ValidateRepo));

            AddSection("仓库");
            AddInputRow("owner/repo", "owner/repo", string.IsNullOrEmpty(repoDraft)
                ? (CloudSavesConfig.IsConfigured
                    ? CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value : "")
                : repoDraft, false, "使用已有仓库", UseExistingRepo);
            AddInputRow("新建仓库名", "mvz2-cloud-saves",
                string.IsNullOrEmpty(createDraft) ? "mvz2-cloud-saves" : createDraft,
                false, "创建新仓库", CreateRepo);

            AddSection("同步");
            AddButtonRow("立即同步", () => CloudSaveService.ManualSync(),
                "上传本地", () => CloudSaveService.ManualUpload(),
                "从云端恢复", () => CloudSaveService.ManualDownload());
            AddButtonRow("创建 Token", new Action(OpenTokenPage),
                "打开仓库", new Action(OpenRepository),
                "刷新", new Action(Rebuild));

            AddParagraph(Loc.T("云存档只在非关卡状态同步关卡进度。关卡内会先记录改动，退出关卡后再上传。"));
        }
        catch (Exception e)
        {
            Diag.Warn("重建云存档页面失败：" + e);
        }
    }

    private void SaveToken()
    {
        var value = StripWhitespace(tokenInput != null ? tokenInput.text : "");
        Diag.Info("UI 保存 token：输入长度=" + value.Length);
        localMessage = string.Empty;
        if (value.Length == 0)
        {
            localMessage = Loc.T("云存档：token 不能为空");
            Toast.Show(localMessage);
            Rebuild();
            return;
        }
        CloudSaveService.SaveToken(value, (ok, message) =>
        {
            localMessage = message ?? string.Empty;
            Toast.Show(message ?? localMessage);
            Rebuild();
        });
    }

    private void PasteToken()
    {
        try
        {
            var text = StripWhitespace(Clipboard.Read());
            if (string.IsNullOrEmpty(text))
            {
                Toast.Show(Loc.T("云存档：剪贴板为空"));
                return;
            }

            var actual = 0;
            if (tokenInput != null)
            {
                // 再次确认没有被原版输入框素材的 16 字符上限截断。
                try { tokenInput.characterLimit = 0; } catch { }
                tokenInput.text = text;
                actual = tokenInput.text.Length;
            }

            Diag.Info("粘贴 token：剪贴板长度=" + text.Length
                + "，输入框长度=" + actual
                + "，characterLimit=" + SafeCharacterLimit());
            if (actual != text.Length)
                Diag.Warn("粘贴 token 长度不一致：剪贴板 " + text.Length + "，输入框 " + actual);

            var message = Loc.T("云存档：已粘贴 {0} / {1} 个字符", actual, text.Length);
            localMessage = message;
            Toast.Show(message);
            Rebuild();
        }
        catch (Exception e)
        {
            Diag.Warn("读取剪贴板失败：" + e.Message);
            Toast.Show(Loc.T("云存档：剪贴板不可用"));
        }
    }

    private int SafeCharacterLimit()
    {
        try { return tokenInput != null ? tokenInput.characterLimit : -1; }
        catch { return -2; }
    }

    /// <summary>GitHub token 不含空白字符；去掉换行/空格，避免单行输入框只显示第一行。</summary>
    private static string StripWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
                sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private void ClearToken()
    {
        Confirm(Loc.T("清除 Token"), Loc.T("确定要清除已保存的 GitHub token 吗？"), value =>
        {
            if (value)
            {
                CloudSaveService.ClearToken();
                Rebuild();
            }
        });
    }

    private void UseExistingRepo()
    {
        var value = existingInput != null ? existingInput.text : "";
        CloudSaveService.UseExistingRepository(value, (ok, message) =>
        {
            Toast.Show(ok ? Loc.T("云存档：仓库已配置") : message);
            Rebuild();
        });
    }

    private void CreateRepo()
    {
        var value = createInput != null ? createInput.text : "";
        CloudSaveService.CreateRepository(value, (ok, message) =>
        {
            Toast.Show(ok ? Loc.T("云存档：仓库已创建") : message);
            Rebuild();
        });
    }

    private void ConfirmForceUpload()
    {
        Confirm(Loc.T("上传本地覆盖云端"), Loc.T("这会用本地存档覆盖云端，确定继续吗？"), value =>
        {
            if (value)
                CloudSaveService.ForceUpload();
        });
    }

    private void ConfirmForceDownload()
    {
        Confirm(Loc.T("从云端覆盖本地"), Loc.T("这会用云端存档覆盖本地（会先备份），确定继续吗？"), value =>
        {
            if (value)
                CloudSaveService.ForceDownload();
        });
    }

    private void ValidateRepo()
    {
        if (!CloudSavesConfig.IsConfigured)
        {
            Toast.Show(Loc.T("云存档：请先配置仓库"));
            return;
        }
        CloudSaveService.UseExistingRepository(CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value, (ok, message) =>
        {
            Toast.Show(ok ? Loc.T("云存档：仓库可用") : message);
            Rebuild();
        });
    }

    private void OpenTokenPage()
    {
        OpenUrl("https://github.com/settings/personal-access-tokens/new");
    }

    private void OpenRepository()
    {
        if (!CloudSavesConfig.IsConfigured)
        {
            Toast.Show(Loc.T("云存档：请先配置仓库"));
            return;
        }
        OpenUrl("https://github.com/" + CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value);
    }

    private void OpenUrl(string url)
    {
        try
        {
            Application.OpenURL(url);
            Toast.Show(Loc.T("云存档：正在打开浏览器"));
        }
        catch (Exception e)
        {
            // 部分 Android 容器（例如 FusionCore）没有映射 OpenURL 的 ICall，
            // 或没有可处理 ACTION_VIEW 的应用。
            Diag.Warn("打开链接失败：" + e.Message);
            Toast.Show(Loc.T("云存档：无法打开浏览器") + "：" + url);
        }
    }

    private void AddSection(string text)
    {
        list?.Add(t =>
        {
            var label = UiKit.Label(t, "Section_" + text, Loc.T(text), 17f);
            return label != null ? label.gameObject : null;
        });
    }

    private void AddParagraph(string text)
    {
        list?.Add(t =>
        {
            var paragraph = UiKit.Paragraph(t, "Paragraph", text ?? string.Empty, 14f);
            return paragraph != null ? paragraph.gameObject : null;
        });
    }

    private void AddInputRow(string label, string placeholder, string initial, bool password,
        string button, Action action)
    {
        list?.Add(t =>
        {
            var row = UiKit.HContainer(t, "Row");
            if (row == null)
                return null;
            UiKit.Fit(row, -1f, UiKit.RowHeight, 1f);

            UiKit.Label(row.transform, "Label", Loc.T(label), UiKit.LabelFontSize, 0f, 0.9f);
            var node = UiSkin.Clone(UiSkinPart.InputField, row.transform, "Input");
            var input = node != null ? node.GetComponent<TMP_InputField>() : null;
            if (input != null)
            {
                // 素材来自原版「输入玩家名字」对话框，prefab 里 m_CharacterLimit = 16。
                // GitHub token 至少 40 个字符（fine-grained 约 90+），不放开就会被
                // 悄悄截断，表现为“token 明明是对的却提示无效”。这里必须显式清 0。
                // 每项单独 try/catch：任何一项失败都不能影响后面的设置。
                try { input.characterLimit = 0; }
                catch (Exception e) { Diag.Warn("设置 characterLimit 失败：" + e.Message); }
                try { input.lineType = TMP_InputField.LineType.SingleLine; }
                catch (Exception e) { Diag.Warn("设置 lineType 失败：" + e.Message); }
                try { input.restoreOriginalTextOnEscape = true; }
                catch (Exception e) { Diag.Warn("设置 restoreOriginalTextOnEscape 失败：" + e.Message); }
                try { input.richText = false; }
                catch (Exception e) { Diag.Warn("设置 richText 失败：" + e.Message); }
                try
                {
                    input.contentType = password
                        ? TMP_InputField.ContentType.Password
                        : TMP_InputField.ContentType.Standard;
                }
                catch (Exception e) { Diag.Warn("设置 contentType 失败：" + e.Message); }
                try { input.text = initial ?? string.Empty; }
                catch (Exception e) { Diag.Warn("设置输入框文本失败：" + e.Message); }
                UiKit.Fit(node, 260f, UiKit.RowHeight, 1f);
                if (password)
                    tokenInput = input;
                else if (label.Contains("owner", StringComparison.OrdinalIgnoreCase))
                    existingInput = input;
                else
                    createInput = input;
            }

            UiKit.TextButton(row.transform, "Action", Loc.T(button), action, UiKit.ButtonWidth * 0.7f);
            return row;
        });
    }

    private void AddButtonRow(params object[] args)
    {
        list?.Add(t =>
        {
            var row = UiKit.HContainer(t, "Buttons");
            if (row == null)
                return null;
            UiKit.Fit(row, -1f, UiKit.RowHeight, 1f);
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                var label = args[i] as string ?? string.Empty;
                var action = args[i + 1] as Action;
                if (action != null)
                    UiKit.TextButton(row.transform, "Btn" + i, Loc.T(label), action, UiKit.ButtonWidth * 0.7f);
            }
            return row;
        });
    }

    private void Confirm(string title, string desc, Action<bool> done)
    {
        // AddonPage 的页面壳挂在 GlobalUICanvas/Dialogs 下，而且是在官方对话框之后
        // 创建的兄弟节点，所以它会盖住 MainSceneController.ShowDialogSelect 的弹窗。
        // 原生确认框出现期间先把自己的页面暂停，回调后再恢复。
        var root = Root;
        var wasActive = root != null && root.activeSelf;
        if (root != null)
        {
            try { root.SetActive(false); } catch { }
        }

        Action<bool> restore = value =>
        {
            if (root != null)
            {
                try { root.SetActive(wasActive); } catch { }
            }
            done(value);
        };

        try
        {
            var main = MainManager.Instance;
            if (main == null || main.Scene == null)
            {
                restore(false);
                return;
            }
            var action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(restore);
            main.Scene.ShowDialogSelect(title, desc, action);
            try
            {
                var dialog = UiGame.Dialog;
                if (dialog != null && dialog.transform != null)
                    dialog.transform.SetAsLastSibling();
            }
            catch
            {
            }
        }
        catch (Exception e)
        {
            Diag.Warn("显示确认框失败：" + e.Message);
            restore(false);
        }
    }
}
