
; (function ($, undefined) {
    'use strict';

    $.extend({
        queryString: {
            parse: function (str) {
                /*!
                    https://github.com/sindresorhus/query-string
                    by Sindre Sorhus
                    MIT License
                */
                var set = typeof str === 'undefined';

                if (set && $.queryString.parsed)
                    return $.queryString.parsed;

                var segments = (set ? window.location.search : str).replace(/^\?/, '').split('&');

                var ret = {};
                segments.forEach(function (param) {
                    var parts = param.replace(/\+/g, ' ').split('=');
                    var key = parts[0];
                    var val = parts[1];
                    if (!(key && val))
                        return;

                    key = decodeURIComponent(key);
                    // missing `=` should be `null`:
                    // http://w3.org/TR/2012/WD-url-20120524/#collect-url-parameters
                    val = typeof val === 'undefined' ? null : decodeURIComponent(val);

                    if (!ret.hasOwnProperty(key)) {
                        ret[key] = val;
                    } else if (Array.isArray(ret[key])) {
                        ret[key].push(val);
                    } else {
                        ret[key] = [ret[key], val];
                    }
                });

                var parsed = {
                    query: ret,
                    get: function (key, ignoreCase) {
                        var ign = !!ignoreCase;
                        if (ign)
                            key = key.toUpperCase();

                        var keys = Object.keys(ret);
                        for (var i in keys) {
                            var prop = keys[i];
                            if (ign && key === prop.toUpperCase()
                                || !ign && key === prop) {
                                return ret[prop];
                            }
                        }
                    }
                };

                if (set)
                    $.queryString.parsed = parsed;

                return parsed;
            }
        },
        stringFormat: function () {
            var args = arguments;
            if (args.length == 0 || typeof args[0] !== 'string')
                return '';
            return args[0].replace(/{(\d+)}/g, function (match, number) {
                var index = parseInt(number);
                return typeof args[index + 1] !== 'undefined'
                  ? args[index + 1]
                  : match;
            });
        }
    });

    $.queryString.query = $.queryString.parse().query;
    $.queryString.get = function (key, ignoreCase) {
        return $.queryString.parse().get(key, ignoreCase);
    };

})(window.jQuery);

; window.jQuery(function ($) {
    'use strict';

    $('#md').html(marked($('#md').text()));
    $('.focus :text').focus();
    $('.focus :text[name=query]').val($.queryString.get('query', true));
    // copy to clipboard
    $('.copy-clip').each(function () {
        var $btn = $(this),
            $text = $btn.parent().parent().find(':text');

        $text.click(function () {
            $text.select();
        });
        $btn.click(function () {
            $text.select();
            document.execCommand("copy");
            this.setAttribute("aria-label", $btn.data('copied'));
            $btn.blur();
        });
        $btn.mouseleave(function () {
            this.setAttribute("aria-label", $btn.data('tips'));
        });
    });
    // switch git url
    $('[data-giturl]').click(function () {
        var $this = $(this),
            $group = $this.closest('.input-group'),
            url = $this.data('giturl'),
            type = $this.text();
        $group.find(':text').val(url);
        $group.find('button:first').html(type + ' <span class="caret"></span>');
    });
    // prevent all empty link
    $('a[href=#]').click(function () {
        event.preventDefault();
    });
    $('.branch-from, .branch-to').each(function () {
        var $con = $(this);
        $con.find('.tab-content li').click(function () {
            var $bn = $con.find('.branch-name'),
                $li = $(this);
            $bn.text($li.text());
            $bn.prev('i').attr('class',
                $li.closest('div').attr('id').indexOf('branch') == 0 ? 'glyphicon glyphicon-random' : 'glyphicon glyphicon-tag');
            $con.find('.dropdown').removeClass('open');
        });
    });
    $('.branch-compare').click(function () {
        var from = $('.branch-from .branch-name').text().trim().replace(/\//g, ';'),
            to = $('.branch-to .branch-name').text().trim().replace(/\//g, ';'),
            relative = escape(from) + '...' + escape(to),
            base = window.location.href,
            seg = base.split('/'),
            last = seg[seg.length - 1],
            append = last != '' && last.indexOf('...') == -1;

        append
        ? window.location.href += '/' + relative
        : window.location.href = relative;
    });

    $(".switch input:checkbox").bootstrapSwitch();
    // blame page
    $('[data-brush]').each(function () {
        var $section = $(this),
            $blocks = $section.find('.no-highlight'),
            brush = $section.data('brush'),
            language = hljs.getLanguage(brush),
            state = null;
        if (!language)
            return;
        $blocks.each(function (i, e) {
            var $this = $(this),
                result = hljs.highlight(brush, $this.text(), true, state);
            state = result.top;
            $(e).html(result.value);
        })
    });
    // delete a branch
    $('[data-branch]').click(function () {
        var $this = $(this),
            name = $this.data('branch');
        if (confirm($.stringFormat(deleteBranch_params.words, name))) {
            $this.disabled = true;
            $.post(deleteBranch_params.url, { path: name }, function () {
                $this.closest('tr').remove();
            });
        }
    });
    // delete a tag
    $('[data-tag]').click(function () {
        var $this = $(this),
            name = $this.data('tag');
        if (confirm($.stringFormat(deleteTag_params.words, name))) {
            $this.disabled = true;
            $.post(deleteTag_params.url, { path: name }, function () {
                $this.closest('div .row').remove();
            });
        }
    });
    // init repository
    $('label[data-repo-init]').click(function () {
        var $this = $(this),
            how = $this.data('repo-init');
        $this.parent().siblings('input').val(how);
        $('div[data-repo-init]').each(function () {
            var $panel = $(this),
                belong = $panel.data('repo-init');
            how == belong
            ? $panel.collapse('show')
            : $panel.collapse('hide');
        });
    });
    // chooser
    typeof chooser_params !== 'undefined'
        && chooser_params instanceof Array
        && chooser_params.forEach(function (params) {
            /*
                params ->
                    data        -> data
                    controller  -> controller for anchor
                    container   -> container selector
                    add_label   -> add
                    del_label   -> remove
                    use_ret_val -> if true, show return value as text
                    add_action
                        -> [action object]
                    del_action
                        -> [action object]
                    action_array-> [] several objects
                        -> four pair and four objects with three name (checked, unchecked)
                        -> key
                        -> on_label
                        -> off_label
                        -> other each object
                            -> [action object]

                [action object]
                    -> url
                    -> query : function (item){} // return a json object such as {team: team, user: item, act: "del"}
            */
            var $container = $(params.container),
                $searcher = $('<input type="text" autocomplete="off">'),
                $add_btn = $('<button type="button" class="btn btn-primary">' + params.add_label + '</button>'),
                $alert_holder = $('<div class="alert_placeholder">'),
                $grid = $('<div>');

            var warning = function (message) {
                $alert_holder.html('<div class="alert alert-danger"><a class="close" data-dismiss="alert">&times;</a><span>' + message + '</span></div>')
            };
            var clearWarning = function () {
                $alert_holder.html('');
            };
            var parseResponseJson = function (text) {
                try {
                    return JSON.parse(text);
                } catch (e) {
                    return 'Connection error';
                }
            };
            var add_row = function (item) {
                var row_html = '<div class="row border-area">',
                    alter_row_html = '<div>',
                    name_html = '<p class="lead">' + (params.controller
                            ? '<a href="/' + params.controller + '/Detail/' + item.Name + '">' + item.Name + '</a>'
                            : item.Name) + '</p>',
                    remover_html = '<a href="#" class="remover btn btn-danger">' + params.del_label + '</a>',
                    $row = $(row_html),
                    $first_row = $(alter_row_html),
                    $second_row = $(alter_row_html),
                    $nameref = $(name_html),
                    $remover = $(remover_html);

                $remover.click(function () {
                    clearWarning();
                    var $mask = $('<div class="disable-mask">');
                    $row.append($mask);
                    event.preventDefault();
                    var xhr = $.post(params.del_action.url, params.del_action.query(item.Name), function (data) {
                        $row.remove();
                    })
                    .fail(function () {
                        $mask.remove();
                        warning(parseResponseJson(xhr.responseText));
                    });
                });

                console.log(item);

                params.action_array.forEach(function (action) {
                    var cell_html = '<div class="col-md-3"><input type="checkbox" /></div>',
                        $cell = $(cell_html),
                        $checkbox = $cell.find('input');

                    $checkbox.bootstrapSwitch({ state: item[action.key], size: 'small', onText: action.on_label, offText: action.off_label });

                    var tobe = null;
                    $checkbox.on('switchChange.bootstrapSwitch', function (event, state) {
                        if (tobe == state) {
                            tobe = null;
                            return;
                        }
                        $checkbox.bootstrapSwitch('readonly', true);
                        clearWarning();
                        var xhr = $.post(state ? action.checked.url : action.unchecked.url,
                            state ? action.checked.query(item.Name) : action.unchecked.query(item.Name),
                            function (data) { })
                        .fail(function () {
                            tobe = !state;
                            $checkbox.bootstrapSwitch('state', tobe);
                            warning(parseResponseJson(xhr.responseText));
                        })
                        .always(function () { $checkbox.bootstrapSwitch('readonly', false); });
                    });

                    $second_row.append($cell);
                });

                $first_row.append($nameref);
                $first_row.append($remover);

                $row.append($first_row);
                $row.append($second_row);

                $grid.append($row);
            };
            params.controller && $searcher.typeahead({
                source: function (query, process) {
                    return $.post('/' + params.controller + '/Search', { query: query }, function (data) {
                        return process(data);
                    });
                },
                items: 10
            });
            $add_btn.click(function () {
                clearWarning();
                var name = $searcher.val();
                var xhr = $.post(params.add_action.url, params.add_action.query(name), function (data) {
                    add_row(params.use_ret_val ? { Name: data } : $.extend({ Name: name }, data));
                    $searcher.val('');
                })
                .fail(function () {
                    warning(parseResponseJson(xhr.responseText));
                });
            });

            var $row = $('<div class="form-group">');

            $searcher.addClass("form-control");
            $row.append($searcher.wrap('<div class="input-group">').parent());
            $('.input-group', $row).append($add_btn.wrap('<span class="input-group-btn">').parent());

            $container.append($row);
            $container.append($alert_holder.wrap('<div>').parent());
            $container.append($grid.wrap('<div>').parent());

            params.data.forEach(function (item) { add_row(item) });
        });
});
