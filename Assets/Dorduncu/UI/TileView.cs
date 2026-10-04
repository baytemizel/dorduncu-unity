// One okey tile drawn with UGUI: ivory body, colored number with the dot underneath, green diamond for the fake okey.
// Rack tiles can be dragged (rearrange / throw), double-clicked (throw) — the TableScreen decides what a gesture means.
using System;
using Dorduncu.Engine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dorduncu.UI
{
    public sealed class TileView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Tile Tile { get; private set; }
        public RectTransform Rect => (RectTransform)transform;
        public bool Draggable;
        public Action<TileView> Clicked, DoubleClicked, DragStarted, Dragged, Dropped;
        public Vector2 Home;                       // resting position inside the parent
        private Image _glow;
        private Text _num;
        private Image _dot, _diamond, _diamondInner, _body, _band;
        private float _glowUntil;

        public static TileView Create(Transform parent, float w, float h)
        {
            var rt = Ui.Node("Tile", parent).Place(0, 0, w, h);
            var v = rt.gameObject.AddComponent<TileView>();
            v._glow = Ui.Img(rt, "Glow", Ui.RoundSmall, new Color(1f, 0.85f, 0.3f, 0f));
            v._glow.rectTransform.Stretch(-7);
            var shadow = Ui.Img(rt, "Shadow", Ui.RoundSmall, new Color(0, 0, 0, 0.35f));
            shadow.rectTransform.Stretch();
            shadow.rectTransform.anchoredPosition = new Vector2(3, -4);
            v._band = Ui.Img(rt, "Band", Ui.RoundSmall, Pal.IvoryShade, true);
            v._band.rectTransform.Stretch();
            v._body = Ui.Img(rt, "Body", Ui.RoundSmall, Pal.Ivory);
            var b = v._body.rectTransform;
            b.anchorMin = new Vector2(0, 0.12f); b.anchorMax = Vector2.one; b.offsetMin = Vector2.zero; b.offsetMax = Vector2.zero;
            v._num = Ui.Txt(rt, "", Mathf.RoundToInt(h * 0.5f), Color.black);
            var n = v._num.rectTransform;
            n.anchorMin = new Vector2(0, 0.38f); n.anchorMax = new Vector2(1, 0.98f); n.offsetMin = n.offsetMax = Vector2.zero;
            v._dot = Ui.Img(rt, "Dot", Ui.Circle, Color.black);
            v._dot.rectTransform.Place(0, -h * 0.2f, h * 0.09f, h * 0.09f);
            v._diamond = Ui.Img(rt, "Fake", Ui.Diamond, Pal.Hex("1F8A4C"));
            v._diamond.rectTransform.Place(0, h * 0.06f, w * 0.62f, w * 0.62f);
            v._diamondInner = Ui.Img(v._diamond.transform, "Inner", Ui.Diamond, Pal.Ivory);
            v._diamondInner.rectTransform.Place(0, 0, w * 0.26f, w * 0.26f);
            return v;
        }

        public void Show(Tile t)
        {
            Tile = t;
            gameObject.name = t == null ? "Tile (back)" : "Tile " + t;
            bool face = t != null;
            _num.enabled = face && !t.Fake;
            _dot.enabled = face && !t.Fake;
            _diamond.enabled = face && t.Fake;
            if (face && !t.Fake)
            {
                var c = Pal.TileColor(t.Color);
                _num.text = t.Value.ToString();
                _num.color = c;
                _dot.color = c;
            }
        }

        /// <summary>Gold glow (hint / finishing tile). seconds ≤ 0 = until cleared.</summary>
        public void Glow(bool on, float seconds = 0)
        {
            _glowUntil = on && seconds > 0 ? Time.time + seconds : 0;
            var c = _glow.color; c.a = on ? 0.95f : 0; _glow.color = c;
        }

        private void Update()
        {
            if (_glowUntil > 0 && Time.time > _glowUntil) Glow(false);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (e.clickCount >= 2) DoubleClicked?.Invoke(this); else Clicked?.Invoke(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!Draggable) return;
            transform.SetAsLastSibling();
            DragStarted?.Invoke(this);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Draggable) return;
            var parent = (RectTransform)transform.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var local))
                Rect.anchoredPosition = local;
            Dragged?.Invoke(this);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!Draggable) return;
            Dropped?.Invoke(this);
        }
    }
}
