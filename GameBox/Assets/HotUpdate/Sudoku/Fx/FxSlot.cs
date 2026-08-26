using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// UI 粒子槽位(Phase 9 真机修复:UGUI RawImage 单粒子,替换原 ParticleSystem 方案)。
    /// 挂在池化粒子 GameObject 上(RectTransform+RawImage 由 FxPool.Rent 创建):
    /// Play 时从起点沿随机方向散开,放大 + alpha 淡入淡出,动画结束调 FxPool.Release 自动回池。
    /// 坐标系:父级 FxRoot 为无 CanvasScaler 的 overlay canvas,1 世界单位 = 1 屏幕像素。
    /// 性能:粒子为 RawImage 静态批处理(同纹理同材质),数量少(≤64),协程内零每帧分配。
    /// 注意:raycastTarget=false 关键——特效 canvas 在最上层(sortingOrder=1000),
    /// 若拦截点击会吃掉棋盘/按钮输入。
    /// </summary>
    public sealed class FxSlot : MonoBehaviour
    {
        RectTransform _rt;
        RawImage _image;

        /// <summary>所属池的贴图地址(Release 时 O(1) 回池,避免名字匹配之类的脆弱做法)。</summary>
        internal string PoolKey;

        // 单粒子动画参数(对应原 ParticleSystem 手感:寿命 0.55s、位移 3.2 单位/秒)
        const float Life = 0.55f;       // 粒子寿命(秒)
        const float BaseSize = 48f;     // 基准直径(px,× scale 缩放)
        const float BaseSpeed = 130f;   // 基准散开速度(px/s,× scale 缩放)

        void Awake()
        {
            _rt = (RectTransform)transform;
            _image = GetComponent<RawImage>();
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f); // 锚点居中:位置即粒子中心
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _image.raycastTarget = false; // 特效不拦截点击(见类注释)
        }

        /// <summary>
        /// 配置并播放一次粒子:起点(FxRoot 世界坐标 = 屏幕像素)、缩放、tint 上色与纹理。
        /// 动画:随机方向散开 + 放大 + 淡入淡出,结束后自动回池。
        /// </summary>
        public void Play(Vector3 fxPos, float scale, Color tint, Texture2D tex)
        {
            transform.position = fxPos;
            _image.texture = tex;
            _image.color = tint; // Kenney 白色图形贴图,色相由 tint 乘算上色
            gameObject.SetActive(true);

            // 随机散开方向与速度(爆发感:中心向四周),尺寸也带随机避免整齐划一
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float speed = BaseSpeed * scale * Random.Range(0.7f, 1.3f);
            float size = BaseSize * scale * Random.Range(0.6f, 1.2f);

            StopAllCoroutines(); // 防御:异常复用前清理(理论不可达)
            StartCoroutine(Animate(dir * speed, size));
        }

        /// <summary>粒子动画协程:位移 + 放大 + alpha 淡入淡出,结束回池。</summary>
        IEnumerator Animate(Vector2 velocity, float size)
        {
            float elapsed = 0f;
            while (elapsed < Life)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / Life;

                transform.position += (Vector3)(velocity * Time.deltaTime); // 恒定速度散开
                float grow = Mathf.Min(1f, t / 0.25f);                      // 前 25% 时长线性放大到峰值
                _rt.sizeDelta = new Vector2(size * grow, size * grow);
                float alpha = t < 0.1f ? t / 0.1f                          // 10% 淡入
                    : (t > 0.65f ? 1f - (t - 0.65f) / 0.35f : 1f);         // 65% 起淡出
                var c = _image.color;
                c.a = alpha;
                _image.color = c;

                yield return null;
            }
            FxPool.Release(this); // 动画播放完毕:自动回池
        }
    }
}
