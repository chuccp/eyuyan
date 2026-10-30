using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;


namespace Yy.Studio;

/// <summary>
/// 新建、打开、保存、关闭及项目文件命令。
/// </summary>
public partial class MainWindow : Window
{
    private void OnNewFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is not null)
        {
            try
            {
                var sourcePath = _project.CreateSourceFile();
                OpenWorkspace(_project.DirectoryPath);
                OpenDocument(sourcePath);
                SetStatus($"已添加项目源码：{Path.GetFileName(sourcePath)}", ReadyBrush);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"无法创建项目源码：\n{exception.Message}",
                    "新建源码",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        AddNewDocument(StudioCatalog.StarterCode);
        OutputBox.Clear();
        VariableList.ItemsSource = null;
        RunInfoText.Text = string.Empty;
        SetStatus("已新建源码标签", ReadyBrush);
    }

    private void OnProjectPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show(
                "请先打开一个 .yyproj 项目。",
                "项目属性",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ProjectPropertiesWindow(_project) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.UpdatedProject is null)
        {
            return;
        }

        _project = dialog.UpdatedProject;
        OpenWorkspace(_project.DirectoryPath);
        SetStatus($"项目属性已保存：{_project.Name}", ReadyBrush);
    }

    private void OnNewProject(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "选择新项目的位置和名称",
            Filter = "易语项目 (*.yyproj)|*.yyproj",
            DefaultExt = ".yyproj",
            AddExtension = true,
            FileName = "新建易语项目.yyproj",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            MessageBox.Show(
                "项目文件已经存在。请使用“打开文件夹”打开已有项目。",
                "新建项目",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectName = Path.GetFileNameWithoutExtension(dialog.FileName);
        try
        {
            var project = YyProject.CreateProject(dialog.FileName, projectName);
            OpenWorkspace(project.DirectoryPath);
            SetStatus($"已创建项目：{project.Name}", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                $"无法创建项目：\n{exception.Message}",
                "新建项目",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnNewForm(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is not null)
        {
            try
            {
                var created = _project.CreateForm("新窗体");
                OpenWorkspace(_project.DirectoryPath);
                OpenDocument(created.FormPath);
                SetStatus($"已创建项目窗体：{Path.GetFileName(created.FormPath)}", ReadyBrush);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"无法创建项目窗体：\n{exception.Message}",
                    "新建窗体",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var document = new EditorDocument(
            null,
            NextUntitledFormName(),
            YyFormSerializer.CreateEmptyDocument("易语窗体"));
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
        SetStatus("已新建窗体标签", ReadyBrush);
    }

    /// <summary>“新建”程序类型对话框(对齐官方:点新建弹出类型选择,确定后内存中直接建程序)。</summary>
    private void OnNewProgramClick(object sender, RoutedEventArgs e) => ShowNewProgramDialog();

    private void ShowNewProgramDialog()
    {
        var dialog = new NewProgramDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.SuppressOnStartup && !_settings.NoNewProgramDialog)
        {
            _settings.NoNewProgramDialog = true;
            _settings.Save();
        }

        switch (dialog.SelectedKind)
        {
            case NewProgramDialog.ProgramKind.Window:
                CreateBlankWindowProgram();
                break;
            case NewProgramDialog.ProgramKind.Console:
                AddNewDocument(
                    ".版本 1\n.程序集 控制台程序\n\n' 控制台程序入口\n输出(\"你好，易语！\")\n");
                SetStatus("已新建易语控制台程序", ReadyBrush);
                break;
            case NewProgramDialog.ProgramKind.Module:
                AddNewDocument(
                    ".版本 1\n.程序集 模块1\n\n' 易模块:在子程序表把“易包”设为“真”后可编译导出\n子程序 示例函数(参数: 文本): 文本\n    返回 参数\n结束子程序\n");
                SetStatus("已新建易语模块", ReadyBrush);
                break;
            case NewProgramDialog.ProgramKind.Project:
                NewProjectCommand.Execute(null, this);
                break;
            case NewProgramDialog.ProgramKind.OpenFile:
                OpenFileCommand.Execute(null, this);
                break;
            case NewProgramDialog.ProgramKind.Sample when dialog.SelectedSamplePath is not null:
                OpenStartupPath(dialog.SelectedSamplePath);
                break;
        }
    }

    /// <summary>
    /// 官方“Windows 窗口程序”:内存中立即创建 _启动窗口 空白程序(窗体+用户代码),
    /// 不弹任何保存对话框,直接进入窗体设计器。
    /// </summary>
    private void CreateBlankWindowProgram()
    {
        var formName = "_启动窗口";
        var form = new YyFormModel
        {
            Name = formName,
            CodeFile = "未命名窗体.yyc",
            Title = "窗口1",
            Width = 500,
            Height = 340
        };
        var formDocument = new EditorDocument(
            null,
            NextUntitledFormName(),
            YyFormSerializer.Serialize(form));
        var codeModel = new YyCodeDocument { Name = "窗口程序集1" };
        var codeDocument = new EditorDocument(
            null,
            "未命名窗体.yyc",
            YyCodeSerializer.Serialize(codeModel));
        _documents.Add(codeDocument);
        _documents.Add(formDocument);
        DocumentTabs.SelectedItem = formDocument;
        SetStatus("已新建易语窗口程序：_启动窗口", ReadyBrush);
    }

    private void OnOpenFile(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开易语文件",
            Filter = "易语文件 (*.yyc;*.yyx)|*.yyc;*.yyx|结构化代码 (*.yyc)|*.yyc|窗体设计 (*.yyx)|*.yyx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OnOpenFolder(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "打开易语项目文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenWorkspace(dialog.FolderName);
        }
    }

    private void OnSaveFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            SaveDocument(_currentDocument);
        }
    }

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            SaveDocumentAs(_currentDocument);
        }
    }

    private void OnCloseTab(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            CloseDocument(_currentDocument);
        }
    }

}
