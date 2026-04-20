"""Flask web UI for the nine-patch auto-compression tool."""

import base64
import io
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))

from compress import run_full_pipeline
from sample_gen import SAMPLES, get_sample

from flask import Flask, jsonify, request, send_file, send_from_directory

app = Flask(__name__, static_folder='static')


@app.route('/')
def index():
    return send_from_directory(app.static_folder, 'index.html')


@app.route('/api/samples')
def list_samples():
    return jsonify(list(SAMPLES.keys()))


@app.route('/api/sample/<name>')
def get_sample_png(name):
    try:
        img = get_sample(name)
    except ValueError as e:
        return jsonify({'error': str(e)}), 400
    buf = io.BytesIO()
    Image.fromarray(img, 'RGBA').save(buf, format='PNG')
    buf.seek(0)
    return send_file(buf, mimetype='image/png')


@app.route('/api/compress', methods=['POST'])
def compress():
    threshold = float(request.form.get('threshold', '4.0'))
    margin = int(request.form.get('margin', '0'))

    # Get source image
    if 'file' in request.files:
        f = request.files['file']
        img_u8 = _load_png_bytes(f.read())
    elif 'sample' in request.form:
        img_u8 = get_sample(request.form['sample'])
    else:
        return jsonify({'status': 'error', 'reason': 'No file or sample provided'}), 400

    result = run_full_pipeline(img_u8, threshold=threshold, margin=margin)

    if result is None:
        return jsonify({
            'status': 'skipped',
            'reason': f'No valid compression found or savings below threshold',
        })

    m = result['metadata']
    return jsonify({
        'status': 'ok',
        'original_png': _img_to_base64(result['original_u8']),
        'compressed_png': _img_to_base64(result['compressed_u8']),
        'reconstructed_png': _img_to_base64(result['reconstructed_u8']),
        'metadata': {
            'xb': m.xb, 'xe': m.xe, 'yb': m.yb, 'ye': m.ye,
            'comp_xb': m.comp_xb(), 'comp_xe': m.comp_xe(),
            'comp_yb': m.comp_yb(), 'comp_ye': m.comp_ye(),
            'original_w': m.original_w, 'original_h': m.original_h,
            'compressed_w': m.compressed_w, 'compressed_h': m.compressed_h,
            'nx': m.nx, 'ny': m.ny,
            'savings_pct': round(m.savings_pct, 1),
        },
        'error_x': round(m.error_x, 2),
        'error_y': round(m.error_y, 2),
        'error_2d': round(m.error_2d, 2),
        'savings_pct': round(m.savings_pct, 1),
    })


def _load_png_bytes(data: bytes):
    import numpy as np
    img = Image.open(io.BytesIO(data)).convert('RGBA')
    return np.array(img, dtype=np.uint8)


def _img_to_base64(img_u8) -> str:
    buf = io.BytesIO()
    Image.fromarray(img_u8, 'RGBA').save(buf, format='PNG')
    buf.seek(0)
    return base64.b64encode(buf.read()).decode('ascii')


if __name__ == '__main__':
    app.run(debug=True, port=5000)
