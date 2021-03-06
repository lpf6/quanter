﻿using Akka.Actor;
using Akka.Event;
using Quanter.BusinessEntity;
using Quanter.Common;
using Quanter.Market;
using Quanter.Strategy.Risk;
using Quanter.Trader.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Quanter.Strategy
{
    public abstract class BaseStrategyActor : TypedActor, IHandle<StrategyRequest>, IHandle<StrategyResponse>
    {
        private readonly ILoggingAdapter _log = Logging.GetLogger(Context);

        protected ActorSelection persistenceActor = null;
        private ActorSelection tradeActor = null;
        protected List<Securities> secs = new List<Securities>();
        private Dictionary<String, Securities> secDict = new Dictionary<string, Securities>();
        private Dictionary<String, ActorSelection> symbolPriceActors = new Dictionary<string, ActorSelection>();
        private Dictionary<String, float> symbolPrices = new Dictionary<string, float>();
        protected IList<IRiskRule> rules = new List<IRiskRule>();

        protected void AddSecurities(Securities sec)
        {
            if (!secDict.ContainsKey(sec.Symbol))
            {
                _log.Debug("订阅{0}股票价格", sec.Symbol);

                secDict.Add(sec.Symbol, sec);

                String path1 = String.Format("/user/{0}", ConstantsHelper.AKKA_PATH_MARKET_MANAGER);
                var marketActor = Context.ActorSelection(path1);
                // 增加一个关注的股票
                MarketRequest request = new MarketRequest() { Type = MarketRequest.RequestType.ADD_SECURITIES, Body = sec };
                marketActor.Tell(request);
                //var ret = marketActor.Ask<Object>(request, TimeSpan.FromSeconds(2));
                //ret.Wait();

                // 
                String path = String.Format("/user/{0}/{1}", ConstantsHelper.AKKA_PATH_MARKET_MANAGER, sec.Symbol);
                var secActor = Context.ActorSelection(path);
                SecuritiesQuotationRequest req = new SecuritiesQuotationRequest()
                {
                    Type = SecuritiesQuotationRequest.RequestType.WATCH_QUOTEDATA,
                    Body = Desc.Id
                };
                secActor.Tell(req);
                symbolPriceActors.Add(sec.Symbol, secActor);
            }
        }

        protected void RemoveSecurities(Securities sec)
        {
            if (secDict.ContainsKey(sec.Symbol)) {
                secDict.Remove(sec.Symbol);
                symbolPriceActors[sec.Symbol].Tell(new SecuritiesQuotationRequest() { Type = SecuritiesQuotationRequest.RequestType.UNWATCH, Body = Desc.Id });
                symbolPriceActors.Remove(sec.Symbol);
            }
        }

        public BaseStrategyActor(EStrategy strategy)
        {
            this.Desc = strategy;
        }

        public EStrategy Desc { get; private set; }

        public void Handle(StrategyRequest message)
        {
            switch(message.Type)
            {
                case StrategyRequestType.INIT:
                    _init();
                    break;
                case StrategyRequestType.START:
                    start();
                    break;
                case StrategyRequestType.STOP:
                    stop();
                    break;
                case StrategyRequestType.BUY:
                    buyOrder((Order)message.Body);
                    break;
                case StrategyRequestType.SELL:
                    sellOrder((Order)message.Body);
                    break;
                case StrategyRequestType.SETTLEMENT:
                    _settle();
                    break;
                default:
                    otherAction(message);
                    break;
            }
        }

        public void Handle(StrategyResponse message)
        {
            switch(message.Type)
            {
                case StrategyResponse.ResponseType.TICK_ARRIVED:
                    onTickData((TickData)message.Body);
                    break;
                case StrategyResponse.ResponseType.BAR_ARRIVED:
                    onBarData((BarData)message.Body);
                    break;
                case StrategyResponse.ResponseType.QUOTE_ARRIVED:
                    onQuoteData((QuoteData)message.Body);
                    break;
                case StrategyResponse.ResponseType.RUN_ARRIVED:
                    run(message.Body);
                    break;
            }
        }

        protected override void PostStop()
        {
            base.PostStop();
            stop();
        }

        protected void _init()
        {
            _log.Info("加载{0}策略的仓位信息", Desc.Id);
            try {
                String path = String.Format("/user/{0}", ConstantsHelper.AKKA_PATH_PERSISTENCE);
                persistenceActor = Context.ActorSelection(path);
                PersistenceRequest req = new PersistenceRequest() { Type = PersistenceType.FIND, Body = String.Format("from EStrategy where Id={0}", Desc.Id) };
                var ret = persistenceActor.Ask<EStrategy>(req, TimeSpan.FromSeconds(10));
                ret.Wait();
                Desc = ret.Result;
            } catch(Exception e)
            {
                _log.Error("发生异常 {0}", e.StackTrace);
                showLog(String.Format("必须重新启动，策略{0}加载超时", Desc.Id));
            }

            _log.Info("{0}策略连接交易接口", Desc.Id);
            if (Desc.Trader != null) { 
                String tpath = String.Format("/user/{0}/{1}", ConstantsHelper.AKKA_PATH_TRADER, Desc.Trader.Id);
                tradeActor = Context.ActorSelection(tpath);
            } else
            {
                // 默认的trade actor is /user/trader/ths
                // tradeActor = Context.ActorSelection("/user/trader");
            }

            onInit();

        }

        private void _settle()
        {
            // 修改在途数量和可用数量
            foreach(var holder in this.Desc.Holders)
            {
                holder.EnableAmount += holder.IncomeAmount;
                holder.IncomeAmount = 0;
                if(holder.EnableAmount == 0)
                {
                    persistenceActor.Tell(new PersistenceRequest() { Type = PersistenceType.DELETE, Body = holder });
                } else
                {
                    persistenceActor.Tell(new PersistenceRequest() { Type = PersistenceType.UPDATE, Body = holder });
                }
            }
        }


        protected virtual void onInit() {
            // 初始化使用哪些风控

            // 使用的哪种类型的行情数据
            // 初始化配置参数
            // 关注哪些股票
        }

        protected virtual void start()
        {

        }

        protected virtual void stop()
        {

        }

        protected virtual void onTickData(TickData data)
        {

        }

        protected virtual void onBarData(BarData data)
        {

        }
        protected virtual void onQuoteData(QuoteData data)
        {
            _log.Debug("策略{0} 接收到股票 {1}价格", Desc.Id, data.Symbol);
            // 当报价数据到来的时候，更新价格
            foreach(var share in this.Desc.Holders)
            {
                if(share.Symbol == data.Symbol)
                {
                    share.LastPrice = data.CurrentPrice;
                }
            }
        }

        protected virtual void run(object data)
        {

        }

        protected virtual void otherAction(StrategyRequest message)
        {
        }

        protected virtual void buyOrder(Order order)
        {
            this.buySecurities( new Securities(SecuritiesTypes.Stock, order.Symbol), order.Price, order.Amount);
        }

        protected virtual void sellOrder(Order order)
        {
            this.sellSecurities(new Securities(SecuritiesTypes.Stock, order.Symbol), order.Price, order.Amount);
        }

        private void _processRisk(Order order)
        {
            RiskMessage msg = new RiskMessage() { Body = order, Type = RiskMessage.MessageType.ORDER };
            foreach(var rule in rules)
            {
                if(!rule.ProcessMessage(msg))
                {
                    if(rule.Action == RiskActions.CancelOrder)
                    {
                        _log.Warning("风控{0}， 取消{1}订单", rule.Title, order.Symbol);
                        showLog(String.Format("3,风控{0}， 取消{1}订单", rule.Title, order.Symbol));
                        order.Amount = 0;
                        break;
                    }
                }
            }
        }

        protected virtual void showLog(String message)
        {

        }


        #region 子账户的处理

        protected int getCurrentAmount(String symbol)
        {
            foreach (var shi in this.Desc.Holders)
            {
                if (shi.Symbol  == symbol)
                {
                    return shi.CurrentAmount;
                }
            }
            return 0;
        }

        protected int getEnableAmount(String symbol)
        {
            foreach (var shi in this.Desc.Holders)
            {
                if (shi.Symbol == symbol)
                {
                    return shi.EnableAmount;
                }
            }
            return 0;
        }

        protected void buySecurities(Securities securities, float price, int amount)
        {
            // 1、下单
            Order order = _createOrder(securities, price, amount, OrderType.BUY);
            _processRisk(order);
            if(order.Amount == 0)
            {
                return;
            }
            _notifyTrader(order);

            // 2、修改持仓
            bool updated = false;
            // 修改可用数量
            foreach (EStockHolder shi in this.Desc.Holders)
            {
                if (shi.Symbol == securities.Symbol)
                {
                    _log.Debug("更新仓位 {0}, {1}, {2}", securities.Symbol, price, amount);
                    shi.IncomeAmount += amount;
                    shi.CostPrice = 0;
                    updated = true;

                    PersistenceRequest req = new PersistenceRequest() { Type = PersistenceType.UPDATE, Body = shi };
                    persistenceActor.Tell(req);
                }
            }

            // 新开仓
            if (!updated)
            {
                _log.Debug("新开仓 {0}, {1}, {2}", securities.Symbol, price, amount);
                EStockHolder shi = new EStockHolder()
                {
                    Symbol = securities.Symbol,
                    Code = _getCode(securities.Symbol),
                    CostPrice = price,
                    LastPrice = price,
                    IncomeAmount = amount,
                    EnableAmount = 0,
                    Strategy = this.Desc,
                    Name = LastClosePriceDataHelper.Instance.LastClosePriceDatas[securities.Symbol].Name,
                };
                this.Desc.Holders.Add(shi);
                PersistenceRequest req = new PersistenceRequest() { Type = PersistenceType.SAVE, Body = shi };
                persistenceActor.Tell(req);

                // 订阅这个股票价格
                AddSecurities(securities);
            }

            // 修改可用资金
            float usedBanlance = price * amount;
            Desc.EnableBalance -= usedBanlance;
            PersistenceRequest req2 = new PersistenceRequest() { Type = PersistenceType.SAVE, Body = Desc };
            persistenceActor.Tell(req2);
        }

        protected void cancelSecurities(int entrustNo)
        {
            _log.Debug("保存到策略账户 委撤单 策略ID:{0}, 合同号:{1}", Desc.Id, entrustNo);
        }

        protected void sellSecurities(Securities securities, float price, int amount)
        {
            // 下单
            Order order = _createOrder(securities, price, amount, OrderType.SELL);
            _notifyTrader(order);

            // 修改可用数量
            foreach (EStockHolder shi in this.Desc.Holders)
            {
                if (shi.Symbol == securities.Symbol)
                {
                    shi.EnableAmount -= amount;
                    PersistenceRequest req = new PersistenceRequest() { Type = PersistenceType.UPDATE, Body = shi };
                    persistenceActor.Tell(req);
                }
            }

            // 修改可用资金
            float usedBanlance = price * amount;
            Desc.EnableBalance += usedBanlance;
            PersistenceRequest req2 = new PersistenceRequest() { Type = PersistenceType.SAVE, Body = Desc };
            persistenceActor.Tell(req2);

        }

        private Order _createOrder(Securities sec, float price, int amount, OrderType type)
        {
            Order order = new Order
            {
                Amount = amount,
                Price = price,
                Type = type,
                Symbol = sec.Symbol,
                StrategyId = Desc.Id
            };

            return order;
        }

        private void _notifyTrader(Order order)
        {
            _log.Debug("通知交易端下单 策略ID:{0}, 代码:{1}, 证券类别: {2}, 价格:{3}， 数量:{4}", Desc.Id, order.Symbol);

            TradeRequest req = new TradeRequest();
            switch (order.Type)
            {
                case OrderType.BUY:
                    req.Type = TradeRequest.RequestType.BUY;
                    break;
                case OrderType.SELL:
                    req.Type = TradeRequest.RequestType.SELL;
                    break;
            }

            req.SecuritiesOrder = order;

            tradeActor.Tell(req); // XXX: 改为ASK?
        }

        private void _updateAccountInfo ()
        {
            // Ask 账户持股信息，资金信息

            // 
        }

        /// <summary>
        /// 由Persistence Actor来做,迁移到Trader 接口去保存订单
        /// </summary>
        /// <param name="order"></param>
        //private void _saveEntrustOrder(Order order)
        //{
        //    PersistenceRequest req = new PersistenceRequest() { Type = PersistenceType.SAVE, Body = order };

        //    persistenceActor.Tell(req);
        //}

        #endregion

        protected string _getSymbol(String stockCode)
        {
            if (stockCode.Length == 6)
            {
                switch (stockCode.Substring(0, 2))
                {
                    case "51":
                    case "50":
                    case "60":
                        stockCode = stockCode + ".XSHG";
                        return stockCode;

                    case "00":
                    case "15":
                    case "16":
                    case "30":
                        stockCode = stockCode + ".XSHE";
                        return stockCode;
                }
            }
            else if (stockCode.Length == 8)
            {
                switch (stockCode.Substring(0, 2))
                {
                    case "sz":
                        stockCode = stockCode.Substring(2, 6) + ".XSHE";
                        break;
                    case "sh":
                        stockCode = stockCode.Substring(2, 6) + ".XSHG";
                        break;
                }
            }

            return stockCode;
        }

        protected string _getCode(String code)
        {
            switch (code.Length)
            {
                case 6:
                    return code;
                case 8:
                    return code.Substring(2);
                case 11:
                    return code.Substring(0, 6);
                default:
                    return code;
            }
        }
    }
}
