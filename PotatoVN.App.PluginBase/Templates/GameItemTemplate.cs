using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml;

namespace PotatoVN.App.PluginBase.Templates;

public static class GameItemTemplate
{
    public static DataTemplate GetTemplate()
    {
        // 使用 XamlReader 动态加载模板。
        // 注意：这里使用普通的 Binding (Binding Path=...)，它基于反射，适用于插件环境。
        // ImagePath.Value 和 Name.Value 对应 Galgame 类中的 LockableProperty<T>.Value
        string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <Grid Width='240' Height='360' Margin='10' Background='#2d3440' CornerRadius='8'>
        <Grid.RowDefinitions>
            <RowDefinition Height='*' />
            <RowDefinition Height='Auto' />
        </Grid.RowDefinitions>
        
        <!-- 游戏封面 -->
        <!-- Stretch='UniformToFill' 保证图片填满且保持比例 -->
        <Image Source='{Binding ImagePath.Value}' 
               Stretch='UniformToFill' 
               HorizontalAlignment='Center' 
               VerticalAlignment='Center'/>
        
        <!-- 渐变遮罩，为了让文字更清晰 -->
        <Grid Grid.Row='0' Grid.RowSpan='2' VerticalAlignment='Bottom' Height='80'>
            <Grid.Background>
                <LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                    <GradientStop Color='#00000000' Offset='0'/>
                    <GradientStop Color='#CC000000' Offset='1'/>
                </LinearGradientBrush>
            </Grid.Background>
        </Grid>

        <!-- 游戏标题 -->
        <TextBlock Grid.Row='1' 
                   Text='{Binding Name.Value}' 
                   Foreground='White'
                   FontSize='16'
                   FontWeight='SemiBold'
                   TextTrimming='CharacterEllipsis'
                   TextWrapping='NoWrap'
                   Margin='12,0,12,12'
                   VerticalAlignment='Bottom'
                   HorizontalAlignment='Left'/>
    </Grid>
</DataTemplate>";

        return (DataTemplate)XamlReader.Load(xaml);
    }
}
