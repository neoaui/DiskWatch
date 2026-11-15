using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DiskWatch.App;

public sealed partial class MainWindow
{
    private readonly TabItem glossaryTab = new();

    private UIElement GlossaryPanel()
    {
        using var stream=typeof(MainWindow).Assembly.GetManifestResourceStream("DiskWatch.Terms")
            ?? throw new InvalidOperationException("缺少内嵌名词说明。");
        using var reader=new StreamReader(stream);
        var sections=reader.ReadToEnd().Replace("\r\n","\n").Split("\n## ",StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(section=>{var split=section.IndexOf('\n');return(Title:section[..split].Trim(),Body:section[(split+1)..].Trim());}).ToList();
        var panel=new DockPanel();
        var top=new StackPanel{Margin=new Thickness(0,0,0,12)};
        top.Children.Add(Copy("搜索栏目或术语，展开查看说明。",12,Muted));
        var search=new TextBox{MinHeight=36,Margin=new Thickness(0,8,0,0),ToolTip="搜索名词，例如：占用分类、未解释差额、快照"};
        System.Windows.Automation.AutomationProperties.SetName(search,"搜索名词说明");top.Children.Add(search);DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        var list=new StackPanel();panel.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Render()
        {
            list.Children.Clear();var query=search.Text.Trim();
            foreach(var section in sections.Where(s=>query.Length==0||s.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||s.Body.Contains(query,StringComparison.OrdinalIgnoreCase)))
            {
                var body=new StackPanel();foreach(var paragraph in section.Body.Split("\n\n",StringSplitOptions.RemoveEmptyEntries)){var text=Copy(paragraph,13);text.Margin=new Thickness(0,4,0,10);body.Children.Add(text);}
                var fold=Fold(section.Title,"",body);fold.IsExpanded=query.Length>0;
                var card=Card(fold,new Thickness(14,4,14,4));card.Margin=new Thickness(0,0,0,8);list.Children.Add(card);
            }
            if(list.Children.Count==0)list.Children.Add(Copy("没有匹配的术语，请尝试其他关键词。",13,Muted));
        }
        search.TextChanged+=(_,_)=>Render();Render();return panel;
    }
}
