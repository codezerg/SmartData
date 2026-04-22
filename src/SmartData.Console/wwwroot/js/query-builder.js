var filterIndex = 1;

function addFilter() {
    var template = document.getElementById('filter-template');
    var clone = template.content.cloneNode(true);
    document.getElementById('qb-filters').appendChild(clone);
    filterIndex++;
}

function toggleValueInput(opSelect) {
    var row = opSelect.closest('.qb-filter-row');
    var valueInput = row.querySelector('[data-role="value"]');
    var op = opSelect.value;
    if (op === '$null' || op === '$notnull') {
        valueInput.style.display = 'none';
        valueInput.value = '';
    } else {
        valueInput.style.display = '';
    }
    if (op === '$in' || op === '$nin') {
        valueInput.placeholder = 'val1, val2, val3';
    } else if (op === '$like') {
        valueInput.placeholder = '%pattern%';
    } else {
        valueInput.placeholder = 'Value';
    }
}

function buildFilterJson() {
    var rows = document.querySelectorAll('.qb-filter-row');
    var filters = [];
    rows.forEach(function(row) {
        var col = row.querySelector('[data-role="column"]').value;
        var op = row.querySelector('[data-role="operator"]').value;
        var val = row.querySelector('[data-role="value"]').value;
        if (!col) return;

        var condition = {};
        if (op === '$eq') {
            condition[col] = castValue(col, val);
        } else if (op === '$null') {
            condition[col] = { "$null": true };
        } else if (op === '$notnull') {
            condition[col] = { "$notnull": true };
        } else if (op === '$in' || op === '$nin') {
            var arr = val.split(',').map(function(s) { return castValue(col, s.trim()); });
            condition[col] = {};
            condition[col][op] = arr;
        } else {
            condition[col] = {};
            condition[col][op] = castValue(col, val);
        }
        filters.push(condition);
    });

    if (filters.length === 0) return '{}';
    if (filters.length === 1) return JSON.stringify(filters[0]);
    return JSON.stringify({ "$and": filters });
}

function castValue(col, val) {
    var colTypes = window.__colTypes || {};
    var type = (colTypes[col] || '').toUpperCase();
    if (type.indexOf('INT') >= 0 || type === 'REAL' || type === 'NUMERIC' || type === 'FLOAT' || type === 'DOUBLE') {
        var num = Number(val);
        if (!isNaN(num) && val !== '') return num;
    }
    if (type === 'BOOLEAN' || type === 'BOOL') {
        if (val === 'true' || val === '1') return true;
        if (val === 'false' || val === '0') return false;
    }
    return val;
}

function prepareQuery() {
    document.getElementById('qb-filter-json').value = buildFilterJson();
    document.getElementById('qb-orderByCol').value = document.getElementById('qb-orderByCol-input').value;
    document.getElementById('qb-orderByDir').value = document.getElementById('qb-orderByDir-input').value;
    document.getElementById('qb-limit').value = document.getElementById('qb-limit-input').value || '100';
    return true;
}
