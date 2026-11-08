using System;
using System.Collections.Generic;
using System.Globalization;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [DisallowMultipleComponent]
    public sealed class MoneyGetApp : MonoBehaviour
    {
        public MoneyGetContent content;
        public bool Purchased {get;private set;}
        public bool IsOpen=>window!=null && window.style.display==DisplayStyle.Flex;
        public MoneyGetMarket Market {get;private set;}
        public VisualElement Window=>window;
        readonly List<(VisualElement element,Action action)> actions=new List<(VisualElement,Action)>();
        readonly List<Action> refreshText=new List<Action>();
        readonly List<Label> quotes=new List<Label>(),companies=new List<Label>(),positionLabels=new List<Label>();
        readonly List<VisualElement> stockRows=new List<VisualElement>(),positionRows=new List<VisualElement>(),closeButtons=new List<VisualElement>(),levers=new List<VisualElement>();
        PcMonitorTerminal terminal;
        VisualElement boundRoot,icon,window,chart,longButton,shortButton;
        Label iconName,heading,wallet,news,quoteTitle,chartRange,quantity,order,portfolio,status;
        int selected,shares=1,leverage=1;
        bool stopLoss=true,takeProfit=true;
        float clock,noticeUntil;
        string notice="";
        int lastCredits=-1;
        static readonly Color Good=new Color(.58f,.9f,.7f),Bad=new Color(1,.46f,.4f),Ink=new Color(.88f,.94f,.91f);
        static string T(string en,string ru)=>Loc.Russian?ru:en;
        static string Price(double value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
        static string Signed(int value)=>(value>0?"+":"")+value;

        void Awake(){terminal=GetComponent<PcMonitorTerminal>();if(!content)content=Resources.Load<MoneyGetContent>("MoneyGetContent");EnsureMarket();}
        void EnsureMarket(){if(Market==null && content && content.stocks!=null && content.stocks.Length>0)Market=new MoneyGetMarket(content.stocks,Environment.TickCount^GetInstanceID());}
        void OnEnable()=>Loc.Changed+=Refresh;
        void OnDisable(){Loc.Changed-=Refresh;Close();}
        void OnDestroy(){icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();}
        public bool Purchase()
        {
            EnsureMarket();if(Purchased || !content || Market==null || !terminal || terminal.Credits<content.price)return false;
            terminal.SetCredits(terminal.Credits-content.price);Purchased=true;
            if(terminal.Root!=null && boundRoot!=terminal.Root)Build();Refresh();return true;
        }
        public bool Trade(int stock,int side,int count,int multiplier,bool sl,bool tp)
        {
            if(!Purchased || Market==null)return false;
            var result=Market.Open(stock,side,count,multiplier,sl,tp,terminal.Credits,out int debit);
            if(result!=MoneyGetMarket.Failure.None)
            {
                Say(result==MoneyGetMarket.Failure.NotEnoughCredits?T("NOT ENOUGH CREDITS","НЕДОСТАТОЧНО КРЕДИТОВ"):result==MoneyGetMarket.Failure.AlreadyOpen?T("CLOSE THE EXISTING POSITION FIRST","СНАЧАЛА ЗАКРОЙТЕ ТЕКУЩУЮ ПОЗИЦИЮ"):T("INVALID ORDER","НЕВЕРНАЯ ЗАЯВКА"));return false;
            }
            terminal.SetCredits(terminal.Credits-debit);
            Say(Market.Quotes[stock].definition.company+" ["+(side==1?"LONG":"SHORT")+" "+count+" x"+multiplier+T(" OPENED]"," ОТКРЫТ]"));Refresh();return true;
        }
        public bool ClosePosition(int stock)
        {
            if(!Purchased || !Market.Close(stock,MoneyGetMarket.Exit.Manual,out var result))return false;
            Settle(result);Refresh();return true;
        }
        public void CloseAll(){if(Market==null)return;for(int i=Market.Quotes.Length-1;i>=0;i--)ClosePosition(i);}
        void Settle(MoneyGetMarket.Settlement result)
        {
            terminal.SetCredits((int)Math.Min(int.MaxValue,(long)terminal.Credits+result.payout));
            string reason=result.reason==MoneyGetMarket.Exit.Liquidated?T("LIQUIDATED","ЛИКВИДАЦИЯ"):result.reason==MoneyGetMarket.Exit.StopLoss?"STOP-LOSS":result.reason==MoneyGetMarket.Exit.TakeProfit?"TAKE-PROFIT":T("CLOSED","ЗАКРЫТО");
            Say(Market.Quotes[result.stock].definition.company+" ["+reason+" "+Signed(result.profit)+" CR]");
        }
        void Say(string message){notice=message;noticeUntil=Time.time+7;if(status!=null)status.text=message;}
        public void SetOpen(bool open)
        {
            if(!Purchased || terminal.Root==null)return;if(boundRoot!=terminal.Root)Build();
            window.style.display=open?DisplayStyle.Flex:DisplayStyle.None;if(!open)return;
            GetComponent<ShopApp>()?.Close();GetComponent<SatelliteConsoleApp>()?.Close();GetComponent<RepairMessagesApp>()?.Close();GetComponent<MoonFinderApp>()?.Close();
            GetComponent<CrazyCosmonautsApp>()?.Close();
            GetComponent<RoutePlannerApp>()?.Close();
            window.BringToFront();terminal.Root.Q("screen-cursor")?.BringToFront();Refresh();
        }
        public void Close(){if(window!=null)window.style.display=DisplayStyle.None;}
        void Update()
        {
            if(!terminal || !content || Market==null || terminal.Root==null)return;
            if(boundRoot!=terminal.Root)Build();
            if(Purchased)
            {
                clock+=Time.deltaTime;
                if(clock>=Mathf.Max(.5f,content.quoteInterval))
                {
                    clock%=Mathf.Max(.5f,content.quoteInterval);
                    foreach(var result in Market.Tick())Settle(result);Refresh();
                }
                if(lastCredits!=terminal.Credits)Refresh();
            }
            if(GameSettingsMenu.BlockGameplayInput || terminal.RouteMapOpen || (GetComponent<CrazyCosmonautsApp>()?.IsOpen ?? false))return;
            bool pointer=terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            var point=terminal.PointerPosition;
            bool free=!(GetComponent<ShopApp>()?.IsOpen??false) && !(GetComponent<SatelliteConsoleApp>()?.IsOpen??false) && !(GetComponent<RepairMessagesApp>()?.IsOpen??false) && !(GetComponent<MoonFinderApp>()?.IsOpen??false);
            bool overIcon=Purchased && !IsOpen && free && pointer && icon.worldBound.Contains(point);
            icon.EnableInClassList("pointer-over",overIcon);
            if(IsOpen)
            {
                foreach(var action in actions)action.element.EnableInClassList("mg-hover",pointer && action.element.enabledSelf && action.element.worldBound.Contains(point));
                status.text=Time.time<noticeUntil?notice:T("Quotes update every ","Обновление котировок: ")+content.quoteInterval.ToString("0.#",CultureInfo.InvariantCulture)+T("s. Market runs while the app is closed."," с. Рынок работает и при закрытом приложении.");
            }
            if(!pointer || Mouse.current==null || !Mouse.current.leftButton.wasPressedThisFrame)return;
            if(overIcon){SetOpen(true);return;}
            if(!IsOpen || !window.worldBound.Contains(point))return;
            for(int i=actions.Count-1;i>=0;i--)if(actions[i].element.enabledSelf && actions[i].element.worldBound.Contains(point)){actions[i].action();break;}
        }
        VisualElement Box(string css,VisualElement parent=null){var e=new VisualElement{pickingMode=PickingMode.Ignore};e.AddToClassList(css);parent?.Add(e);return e;}
        Label Text(string text,string css,VisualElement parent){var label=new Label(text){pickingMode=PickingMode.Ignore};if(!string.IsNullOrEmpty(css))label.AddToClassList(css);parent.Add(label);return label;}
        VisualElement Action(VisualElement parent,string name,Func<string> text,Action action,string css="mg-button")
        {
            var e=Box(css,parent);e.name="moneyget-"+name;var label=Text("","",e);
            refreshText.Add(()=>label.text=text());actions.Add((e,action));return e;
        }
        void Build()
        {
            var desktop=terminal.Root.Q("desktop");if(desktop==null)return;
            icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();boundRoot=terminal.Root;
            actions.Clear();refreshText.Clear();quotes.Clear();companies.Clear();positionLabels.Clear();stockRows.Clear();positionRows.Clear();closeButtons.Clear();levers.Clear();
            var style=Resources.Load<StyleSheet>("MoneyGet");if(style && !terminal.Root.styleSheets.Contains(style))terminal.Root.styleSheets.Add(style);
            icon=Box("desktop-icon",desktop);icon.name="moneyget";icon.style.position=Position.Absolute;icon.style.left=590;icon.style.top=175;icon.style.width=200;
            var image=new Image{vectorImage=content.desktopIcon,scaleMode=ScaleMode.ScaleToFit,tintColor=Color.white,pickingMode=PickingMode.Ignore};image.AddToClassList("icon-image");icon.Add(image);iconName=Text("","icon-label",icon);iconName.style.fontSize=17;
            window=Box("mg-window",desktop);window.name="moneyget-window";window.style.display=DisplayStyle.None;
            var header=Box("mg-header",window);heading=Text("","mg-heading",header);heading.style.flexGrow=1;Action(header,"close",()=>"[X]",Close);
            wallet=Text("","mg-wallet",window);news=Text("","mg-news",window);
            var body=Box("mg-body",window);var market=Box("mg-market",body);
            Text("","mg-caption",market).name="moneyget-market-heading";
            for(int i=0;i<Market.Quotes.Length;i++)
            {
                int index=i;var row=Action(market,"stock-"+i,()=>"",()=>{selected=index;Refresh();},"mg-stock");row.Clear();
                companies.Add(Text(Market.Quotes[i].definition.company,"mg-company",row));quotes.Add(Text("","mg-price",row));stockRows.Add(row);
            }
            var trade=Box("mg-trade",body);quoteTitle=Text("","mg-quote-title",trade);
            chart=Box("mg-chart",trade);chart.generateVisualContent+=DrawChart;chartRange=Text("","mg-range",trade);
            var amounts=Box("mg-controls",trade);Action(amounts,"less",()=>"[-]",()=>{shares=Math.Max(1,shares-1);Refresh();});quantity=Text("","mg-quantity",amounts);
            Action(amounts,"more",()=>"[+]",()=>{shares=Math.Min(100,shares+1);Refresh();});Action(amounts,"plus5",()=>"[+5]",()=>{shares=Math.Min(100,shares+5);Refresh();});
            for(int i=1;i<=3;i++){int x=i;levers.Add(Action(amounts,"leverage-"+i,()=>"x"+x,()=>{leverage=x;Refresh();}));}
            var protections=Box("mg-controls",trade);
            Action(protections,"stop",()=>"["+(stopLoss?"x":" ")+"] SL -20%",()=>{stopLoss=!stopLoss;Refresh();});
            Action(protections,"take",()=>"["+(takeProfit?"x":" ")+"] TP +35%",()=>{takeProfit=!takeProfit;Refresh();});
            order=Text("","mg-order",trade);var deals=Box("mg-controls",trade);
            longButton=Action(deals,"long",()=>T("[LONG / BUY]","[ЛОНГ / КУПИТЬ]"),()=>Trade(selected,1,shares,leverage,stopLoss,takeProfit));longButton.AddToClassList("mg-long");
            shortButton=Action(deals,"short",()=>T("[SHORT / SELL]","[ШОРТ / ПРОДАТЬ]"),()=>Trade(selected,-1,shares,leverage,stopLoss,takeProfit));shortButton.AddToClassList("mg-short");
            var pheader=Box("mg-portfolio-header",window);portfolio=Text("","mg-caption",pheader);portfolio.style.flexGrow=1;Action(pheader,"close-all",()=>T("[CLOSE ALL]","[ЗАКРЫТЬ ВСЁ]"),CloseAll);
            for(int i=0;i<Market.Quotes.Length;i++)
            {
                int index=i;var row=Box("mg-position",window);positionRows.Add(row);positionLabels.Add(Text("","mg-position-text",row));
                closeButtons.Add(Action(row,"exit-"+i,()=>T("[CLOSE]","[ЗАКРЫТЬ]"),()=>ClosePosition(index)));
            }
            Text("","mg-risk",window).name="moneyget-risk";status=Text("","mg-status",window);Refresh();terminal.Root.Q("screen-cursor")?.BringToFront();
        }
        void Refresh()
        {
            if(window==null || Market==null)return;lastCredits=terminal.Credits;
            icon.style.display=Purchased?DisplayStyle.Flex:DisplayStyle.None;iconName.text=content.Title;heading.text=content.Title+" ["+T("LUNAR EXCHANGE","ЛУННАЯ БИРЖА")+"]";
            int floating=0,equity=0;foreach(var p in Market.Positions){floating+=Market.Profit(p);equity+=Market.Equity(p);}
            wallet.text=T("CASH ","БАЛАНС ")+terminal.Credits+" CR    "+T("IN POSITIONS ","В ПОЗИЦИЯХ ")+equity+" CR    P/L "+Signed(floating)+" CR    "+T("REALIZED ","ЗАКРЫТО ")+Signed(Market.RealizedProfit)+" CR";
            if(Market.NewsStock<0)news.text=T("MARKET OPEN [Earth uplink unstable. Expect volatility.]","БИРЖА ОТКРЫТА [Связь с Землёй нестабильна. Ожидаются колебания.]");
            else {var def=Market.Quotes[Market.NewsStock].definition;news.text=def.company+" ["+(Market.NewsUp?T(def.goodNews,def.goodNewsRu):T(def.badNews,def.badNewsRu))+"]";}
            window.Q<Label>("moneyget-market-heading").text=T("COMPANY [PRICE] [CHANGE]","КОМПАНИЯ [ЦЕНА] [ИЗМЕНЕНИЕ]");
            for(int i=0;i<Market.Quotes.Length;i++)
            {
                var q=Market.Quotes[i];double change=(q.price/q.opening-1)*100;
                quotes[i].text=Price(q.price)+" CR   "+(change>=0?"+":"")+change.ToString("0.0",CultureInfo.InvariantCulture)+"%";quotes[i].style.color=change>=0?Good:Bad;
                stockRows[i].EnableInClassList("mg-selected",selected==i);
                var p=Market.Find(i);positionRows[i].style.display=p!=null?DisplayStyle.Flex:DisplayStyle.None;
                if(p!=null)
                {
                    positionLabels[i].text=q.definition.company+"  "+(p.side==1?"L":"S")+" "+p.shares+" x"+p.leverage+"  "+Price(p.entry)+" > "+Price(q.price)+"   "+Signed(Market.Profit(p))+" CR";
                    positionLabels[i].style.color=Market.Profit(p)>=0?Good:Bad;
                }
            }
            var current=Market.Quotes[selected];quoteTitle.text=current.definition.company+"  ["+Price(current.price)+" CR]";
            double min=double.MaxValue,max=0;foreach(double value in current.history){min=Math.Min(min,value);max=Math.Max(max,value);}
            chartRange.text="MIN "+Price(min)+"   MAX "+Price(max)+"   "+T("LAST ","ПОСЛЕДНИЕ ")+current.history.Count+T(" QUOTES"," КОТИРОВОК");chart.MarkDirtyRepaint();
            quantity.text=T("SHARES ","АКЦИЙ ")+shares;
            for(int i=0;i<levers.Count;i++)levers[i].EnableInClassList("mg-selected",leverage==i+1);
            int margin=MoneyGetMarket.RequiredMargin(current.price,shares,leverage);
            order.text=T("MARGIN ","ЗАЛОГ ")+margin+" CR  /  "+T("MAX LOSS ","МАКС. УБЫТОК ")+margin+" CR";
            bool can=Purchased && terminal.Credits>=margin && Market.Find(selected)==null;longButton.SetEnabled(can);shortButton.SetEnabled(can);
            portfolio.text=T("OPEN POSITIONS ","ОТКРЫТЫЕ ПОЗИЦИИ ")+Market.Positions.Count+T("  [ENTRY > NOW / P&L]","  [ВХОД > СЕЙЧАС / ПРИБЫЛЬ]");
            window.Q<Label>("moneyget-risk").text=T("LONG: profit on rises. SHORT: profit on falls. SL/TP use margin %. At zero equity: liquidation. Exits use the next quote; no debt.","ЛОНГ: прибыль при росте. ШОРТ: при падении. SL/TP: % от залога. При нуле: ликвидация. Выход по новой котировке; без долга.");
            foreach(var refresh in refreshText)refresh();
        }
        void DrawChart(MeshGenerationContext context)
        {
            var rect=chart.contentRect;if(rect.width<2 || rect.height<2)return;var values=Market.Quotes[selected].history;
            double lo=double.MaxValue,hi=0;foreach(var value in values){lo=Math.Min(lo,value);hi=Math.Max(hi,value);}double range=Math.Max(.1,hi-lo);lo-=range*.1;hi+=range*.1;
            var p=context.painter2D;p.lineWidth=1;p.strokeColor=new Color(.2f,.3f,.25f);
            for(int i=1;i<4;i++){float y=rect.height*i/4;p.BeginPath();p.MoveTo(new Vector2(0,y));p.LineTo(new Vector2(rect.width,y));p.Stroke();}
            p.strokeColor=values[values.Count-1]>=values[0]?Good:Bad;p.lineWidth=1.8f;p.BeginPath();
            for(int i=0;i<values.Count;i++){var point=new Vector2(i*rect.width/(values.Count-1),rect.height-(float)((values[i]-lo)/(hi-lo))*rect.height);if(i==0)p.MoveTo(point);else p.LineTo(point);}p.Stroke();
        }
    }
}
