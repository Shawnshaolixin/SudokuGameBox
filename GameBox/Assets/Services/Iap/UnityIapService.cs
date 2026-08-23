#if SUDOKU_IAP
using System;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace Box.Services
{
    /// <summary>
    /// 内购真实现（Phase 7 7-1）：Unity IAP 4.x（com.unity.purchasing）接入 Google Play，\n    /// 商品为「去广告」非消耗型（remove_ads，08 号文档 §5.1）。\n    /// 职责：初始化商店、发起购买、Android 启动自动恢复（非消耗品）、\n    /// 购买/恢复成功后写入 D-7 存档分区 box.commerce 并触发 PurchaseCompleted（Bootstrap 由此链路去广告）。\n    /// 注意：本文件在 #if SUDOKU_IAP 下编译；商品 ID 与 Google Play 后台（A3 账号）配置保持一致。\n    /// </summary>
    public sealed class UnityIapService : IIapService, IDetailedStoreListener
    {
        // 商品 ID（08 号文档 §5.1）：与 Google Play 控制台配置一致；测试期可用「license testing」账号
        public const string RemoveAdsProductId = "remove_ads";

        private const string CommerceModuleId = "box.commerce"; // D-7 存档分区：去广告状态

        private readonly ISaveService _save;
        private IStoreController _controller; // Unity IAP 商店控制器（初始化成功后可用）

        public bool IsInitialized { get; private set; }

        /// <summary>本地记忆的「是否已购买去广告」（启动时从存档分区恢复，收据校验为权威）。</summary>
        public bool IsRemoveAdsPurchased { get; private set; }

        /// <summary>去广告购买/恢复成功事件（AppBootstrap 订阅后调用 AdsService.SetRemoveAds(true)）。</summary>
        public event Action PurchaseCompleted;

        /// <param name="save">存档服务，用于读写 D-7「box.commerce」分区的去广告状态。</param>
        public UnityIapService(ISaveService save)
        {
            _save = save;
            // 启动时先从 D-7 分区恢复（离线兜底）；发布环境下以收据校验为准（OnInitialized 里检测 hasReceipt）
            IsRemoveAdsPurchased = _save.GetModule<CommerceData>(CommerceModuleId)?.RemoveAdsPurchased ?? false;
        }

        /// <summary>初始化商店并按 Google Play 商店配置商品（主线程、启动时调用一次）。</summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                Debug.Log("[IAP] 已初始化，跳过重复初始化");
                return;
            }

            var builder = ConfigurationBuilder.Instance(
                StandardPurchasingModule.Instance(AppStore.GooglePlay));
            builder.AddProduct(RemoveAdsProductId, ProductType.NonConsumable);

            // 传入本类作为监听器：Unity IAP 异步连接商店后回调 OnInitialized
            UnityPurchasing.Initialize(this, builder);
        }

        /// <summary>发起「去广告」购买。已购买则直接回调（幂等处理）。</summary>
        public void BuyRemoveAds()
        {
            if (!IsInitialized || _controller == null)
            {
                Debug.LogError("[IAP] 商店未初始化完成，无法发起购买");
                return;
            }

            if (IsRemoveAdsPurchased)
            {
                // 已经购买过：无需再次扣费，直接通知链路去广告
                Debug.Log("[IAP] 已购买去广告，直接完成");
                PurchaseCompleted?.Invoke();
                return;
            }

            Debug.Log("[IAP] 发起购买 remove_ads");
            _controller.InitiatePurchase(RemoveAdsProductId);
        }

        /// <summary>
        /// 恢复购买。Android 上 Google Play 对非消耗品自动恢复（启动即回调 ProcessPurchase），\n    /// 因此本方法仅兜底：检查商品收据存在即视为已购（官方 Android 指南：无需主动 Restore）。\n    /// </summary>
        public void RestorePurchases()
        {
            if (!IsInitialized || _controller == null)
            {
                Debug.LogError("[IAP] 商店未初始化完成，无法恢复购买");
                return;
            }

            var product = _controller.products.WithID(RemoveAdsProductId);
            if (product != null && product.hasReceipt && !IsRemoveAdsPurchased)
            {
                Debug.Log("[IAP] 检测到已购收据（Android 自动恢复），完成去广告");
                CompletePurchase(product);
            }
        }

        // ======================== IDetailedStoreListener 回调 ========================

        /// <summary>商店连接成功：保存控制器，并检查非消耗品收据做启动恢复。</summary>
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            IsInitialized = true;
            Debug.Log("[IAP] 商店初始化成功");

            // 启动自动恢复：卸载重装 / 换设备场景下，Google Play 会回调 ProcessPurchase；
            // 此处先行兜底检查收据，避免去广告延迟生效
            RestorePurchases();
        }

        /// <summary>初始化失败（旧版单参数回调，转发到详细版）。</summary>
        public void OnInitializeFailed(InitializationFailureReason error)
        {
            OnInitializeFailed(error, null);
        }

        /// <summary>初始化失败（含错误信息）。</summary>
        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            IsInitialized = false;
            Debug.LogError($"[IAP] 商店初始化失败：{error}（{message}）");
        }

        /// <summary>购买流程处理：仅认可 remove_ads；其余未知商品直接完成，不影响商店状态。</summary>
        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent)
        {
            var product = purchaseEvent.purchasedProduct;
            Debug.Log($"[IAP] 收到购买回调：{product.definition.id}");

            if (product.definition.id == RemoveAdsProductId)
            {
                CompletePurchase(product);
                return PurchaseProcessingResult.Complete;
            }

            Debug.LogWarning($"[IAP] 未知商品：{product.definition.id}，忽略");
            return PurchaseProcessingResult.Complete;
        }

        /// <summary>购买失败（新式详细回调）。用户取消购买是常见情况，仅告警不视为错误。</summary>
        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            Debug.LogWarning($"[IAP] 购买失败：{product.definition.id}，原因 {failureDescription.reason}（{failureDescription.message}）");
        }

        /// <summary>购买失败（旧版回调，转发到详细版）。</summary>
        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogWarning($"[IAP] 购买失败（旧回调）：{product.definition.id}，原因 {failureReason}");
        }

        // ======================== 内部工具 ========================

        /// <summary>
        /// 购买/恢复成功统一出口：写入 D-7「box.commerce」分区并触发 PurchaseCompleted。\n    /// 由此，Bootstrap 订阅该事件调用 AdsService.SetRemoveAds(true)，完成去广告闭环。\n    /// </summary>
        private void CompletePurchase(Product product)
        {
            if (IsRemoveAdsPurchased)
            {
                // 幂等：重复回调（如启动恢复 + 手动再购）不重复写盘/发事件
                PurchaseCompleted?.Invoke();
                return;
            }

            IsRemoveAdsPurchased = true;
            _save.SetModule(CommerceModuleId, new CommerceData
            {
                RemoveAdsPurchased = true,
                UpdatedAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            Debug.Log($"[IAP] 去广告购买完成，已写入存档分区 {CommerceModuleId}");

            PurchaseCompleted?.Invoke();
        }
    }
}
#endif