<?php
/**
 * Plugin Name: MSP Marketing Edge Wordpress AutoBlog 
 * Plugin URI: https://www.mspmarketingedge.com
 * Description: Automatically import your weekly syndicated blog post from the MSP Marketing Edge
 * Version: 1.1.9
 * Author: MSP Marketing Edge
 * Author URI: https://www.mspmarketingedge.com
 * License: GPL2
 */

/* INIT */

define( 'YOUR_TECH_UPDATES_PLUGIN_DIR', plugin_dir_path( __FILE__ ) );
define( 'YOUR_TECH_UPDATES__PLUGIN_VERSION', '1.1.9' );
define( 'YOUR_TECH_UPDATE_VERSION', 'edge_update_version');
define( 'SOCIAL_TOOLS_URL_UAT', 'https://socialtools-staging.mspmarketingedge.com/api/blog/push');
define( 'SOCIAL_TOOLS_URL', 'https://socialtools.mspmarketingedge.com/api/blog/push');
define( 'SOCIAL_TOOLS_AUDIT', true );
define( 'SOCIAL_TOOLS_AUDIT_EMAILS', '');
define( 'YOUR_TECH_UPDATES_DEBUG', false);

require_once( YOUR_TECH_UPDATES_PLUGIN_DIR . 'meta_data_options.php' );
require_once( YOUR_TECH_UPDATES_PLUGIN_DIR . 'meta_data_updater.php' );

if (!defined('ABSPATH')) die();

if (!class_exists('YOUR_TECH_UPDATES'))
{
    class YOUR_TECH_UPDATES  
    {
        
        public function __construct() 
        {
            remove_action( 'wp_head', 'rel_canonical' );
            add_action('wp_head', array($this,'metadata'));
            add_shortcode('get_tech_posts', array($this,'get_tech_posts'));
            add_shortcode('republish_tech_post', array($this,'republish_tech_post'));

            add_filter( 'cron_schedules', array($this,'processSchedule') );
            add_filter( 'wpseo_canonical', array($this,'remove_canonical' ));
            add_filter( 'rank_math/frontend/canonical', array($this,'remove_canonical' ));

            add_action('save_post',  array($this,'save_post'), 10, 3);

            //Schedule an action if it's not already scheduled
            if ( ! wp_next_scheduled( 'tech_updates_cron_hook' ) ) {
                wp_schedule_event( time(), 'four_times_per_day_tech_updates', 'tech_updates_cron_hook' );
            }
            
            add_action( 'tech_updates_cron_hook', array($this,'get_tech_posts') );
        }
        
        public function enqueue() {
        }

        function remove_canonical( $canonical ) 
        {
            global $post;
            if($post == null) { return $canonical;}
            $values = get_post_custom_values( 'edge_url', $post->ID );
            if($values == null) { return $canonical;}
            if(count($values) == 0) { return $canonical; }
            return false;
        }

        function save_post($post_id, $post, $update)
        {
            if( ! ( wp_is_post_revision( $post_id) || wp_is_post_autosave( $post_id ) ) ) 
            {
                $this::upload_social_feed($post_id, $post, "Manual Publish");
            }
        }
        
        function upload_social_feed($post_id, $post, $src)
        {
            return;

            $response = "";
            $params = array();
            try
            {
                $post_type = get_post_type($post_id);
                $current_status = get_post_status ( $post_id );
                
                $status_json = json_encode($current_status);

                if(YOUR_TECH_UPDATES_DEBUG)
                {
                    wp_mail( SOCIAL_TOOLS_AUDIT_EMAILS, "status:".$src.":".$status_json, $status_json."|".$post_type."|".$current_status, 
                    array('Content-Type: text/html; charset=UTF-8') );
                }

                if ($post_type === "post" && $current_status === "publish") {
                    $values = get_post_custom_values( 'edge_url', $post->ID );
                    $week_commencing_arr = get_post_custom_values( 'mspedge_week_commencing', $post->ID );
                    $week_commencing = "";

                    if(isset($week_commencing_arr)) {
                        $week_commencing = $week_commencing_arr[0];
                    }
                    $pubDate = get_the_date("d-m-Y", $post_id);
                    if($pubDate == false) {
                        $pubDate = date("d-m-Y");
                    }

                    if(YOUR_TECH_UPDATES_DEBUG)
                    {
                        wp_mail( SOCIAL_TOOLS_AUDIT_EMAILS, "values:".json_encode($values).":".$pubDate, $week_commencing."|".json_encode($values)."|".$pubDate, 
                        array('Content-Type: text/html; charset=UTF-8') );
                    }

                    if($values != null){
                        $options = get_option( TECH_UPDATE_SETTINGS );
                        $params = array(
                            'id' => $post_id,
                            'accessToken' => '276dd63d-fc05-4b3d-b2b5-040e5e891769',
                            'email' => $options[TECH_UPDATE_LICENSE],
                            'status' => $post->post_status,
                            'excerpt'=> get_the_excerpt($post_id),
                            'url' => get_permalink($post_id),
                            'publishedDate' => $pubDate,
                            'weekCommencing' => $week_commencing
                        );
                        
                        $url = SOCIAL_TOOLS_URL;

                        $response = wp_remote_post( $url, array(
                            'method'      => 'POST',
                            'headers' => array(
                                'Content-Type' => 'application/json',
                            ),
                            'body' => json_encode($params),
                            )
                        );

                        if(YOUR_TECH_UPDATES_DEBUG)
                        {
                                wp_mail( SOCIAL_TOOLS_AUDIT_EMAILS, "values:".json_encode($params).":".$url, json_encode($response), 
                                array('Content-Type: text/html; charset=UTF-8') );
                        }
                        
                        /* REMOVE ONCE LIVE */
                        if (SOCIAL_TOOLS_AUDIT)
                        {
                            $body = "<p>Response:".json_encode($response)."</p>";
                            $body .=  "<p>Params:".json_encode($params)."</p>";
                            $body .=  "<p>Post:".json_encode($post)."</p>";
                            $toemail = SOCIAL_TOOLS_AUDIT_EMAILS;
                            $subject = "Test AutoBlog Feed Send:".$post_id.":".$src;
                            $headers = array('Content-Type: text/html; charset=UTF-8');
                            wp_mail( $toemail, $subject, $body, $headers );
                            /* REMOVE ONCE LIVE */
                        }
                    }
                }
            }
            catch (Exception $ex)
            {
                /* REMOVE ONCE LIVE */
                if (SOCIAL_TOOLS_AUDIT)
                {
                    $body = "<p>Response:".json_encode($response)."</p>";
                    $body .=  "<p>Params:".json_encode($params)."</p>";
                    $body .=  "<p>Post:".json_encode($post)."</p>";
                    $body .=  "<p>Exception:".$ex->getMessage()."</p>";
                    $toemail = SOCIAL_TOOLS_AUDIT_EMAILS;
                    $subject = "Test AutoBlog Feed Send Error:".$post_id;
                    $headers = array('Content-Type: text/html; charset=UTF-8');
                    
                    if(YOUR_TECH_UPDATES_DEBUG)
                    {
                        wp_mail( $toemail, $subject, $body, $headers );
                    }
                }
                /* REMOVE ONCE LIVE */
            }
        }
        

        function image_upload($post_id, $imageurl, $title = "")
        {
            require_once( ABSPATH . "/wp-load.php");
            require_once( ABSPATH . "/wp-admin/includes/image.php");
            require_once( ABSPATH . "/wp-admin/includes/file.php");
            require_once( ABSPATH . "/wp-admin/includes/media.php");
         
            try
            {
                $debug = (YOUR_TECH_UPDATES_DEBUG == true);
                
                $imagetype = "jpeg"; 
                $uniq_name = date('dmY') . str_replace('.', '', microtime(true));
                $filename = $uniq_name.'.'.$imagetype;
    
                $uploaddir = wp_upload_dir();
                $uploadfile = $uploaddir['path'] . '/' . $filename;
                $savefile = fopen($uploadfile, 'w');
                if ($debug) echo "<p>uploadfile=$uploadfile</p>";

                $arrContextOptions=array(
                    "ssl"=>array(
                        "verify_peer"=>false,
                        "verify_peer_name"=>false,
                    ),
                );  

                $data = wp_remote_get($imageurl, array('sslverify' => FALSE));
                $body    = $data['body']; // use the content
                $contents = $body;

                if ($debug) { echo "<p>contents:</p>"; var_dump($contents); }

                fwrite($savefile, $contents);
                fclose($savefile);
                if ($debug) { echo "<p>savefile:</p>"; var_dump($savefile); }
                if ($debug) { echo "<p>remoteImagefile:</p>"; var_dump($imageurl); }

                $wp_filetype = wp_check_filetype(basename($uploadfile), null );
                $arg_tile = basename($imageurl);
                if ($title != "") { $arg_tile = $title; }

                $attachment = array(
                    'post_mime_type' => $wp_filetype['type'],
                    'post_title' => $arg_tile,
                    'post_content' => '',
                    'post_status' => 'inherit'
                );
                if ($debug) { echo "<p>attachment:</p>"; var_dump($attachment); }

                $attach_id = wp_insert_attachment( $attachment, $uploadfile, $post_id);
                if ($debug) { echo "<p>attach_id:</p>"; var_dump($attach_id); }

                $imagenew = get_post( $attach_id );
                if ($debug) { echo "<p>imagenew:</p>"; var_dump($imagenew); }

                $fullsizepath = get_attached_file( $imagenew->ID );
                if ($debug) { echo "<p>fullsizepath:</p>"; var_dump($fullsizepath); }

                $attach_data = wp_generate_attachment_metadata( $attach_id, $fullsizepath );
                if ($debug) { echo "<p>attach_data:</p>"; var_dump($attach_data); }

                $status = wp_update_attachment_metadata( $attach_id, $attach_data ); 
                if ($debug) { echo "<p>status:</p>"; var_dump($status); }

                if ($title != "")
                    update_post_meta($attach_id, '_wp_attachment_image_alt', $title);

                if ($post_id > 0)
                    set_post_thumbnail( $post_id, $attach_id );  // set post thumnail (featured image) for the given post
            }
            catch (Exception $ex)
            {
                echo "<p>Exception:".$ex->getMessage()."</p>";
            }

        }

        
        function metadata()
        {
            global $post;

            if($post == null) { return;}

            $hide_feature_css = "";
            $edge_has_video_values = get_post_custom_values( 'edge_has_video', $post->ID );
            
            if($edge_has_video_values != null)
            {
                if(count($edge_has_video_values) > 0) 
                { 
                    $edge_has_video_value = $edge_has_video_values[0];
                    if ($edge_has_video_value == "true")
                    {
                        $hide_feature_css = "<style>.single-post .elementor-widget-theme-post-featured-image, .single-post .attachment-voip_blog-thumb, .single-post .has-post-thumbnail img.wp-post-image, .single-post .attachment-twentyseventeen-featured-image.wp-post-image, .single-post .has-post-thumbnail .entry-thumb, .single-post .wp-post-image, .single-post .et_post_meta_wrapper img, .single-post .post .media img  { display: none !important; } </style>";
                    }
                }
            }
                
            $values = get_post_custom_values( 'edge_url', $post->ID );
            if($values == null) { return;}

            if(count($values) == 0) { return ; }
                
            $edge_url = $values[0];
   
          if ($edge_url != "")
          {  

              ?>
<meta name="robots" content="noindex">
<link rel="canonical" href="<?php echo $edge_url; ?>" >
<meta name="tech_id" content="<?php echo $post->ID; ?>" >
              <?php 
              echo $hide_feature_css;
          }
        }

        public static function check_license($email)
        {
            $time = strtotime("now");
            $response = wp_remote_get("https://www.mspmarketingedge.com/wp-content/keap/msp_edge_license.php?email=$email&tool=techupdatesplugin&time=$time", array('sslverify' => FALSE));

            if ( is_array( $response ) && ! is_wp_error( $response ) ) 
            {
                $headers = $response['headers']; // array of http header lines
                $body    = $response['body']; // use the content
                $content = json_decode($body);
                return ($content->license_activated == "true");
            }
        }

        function republish_tech_post($atts = [], $content = null, $tag = '') 
        {
            $params = shortcode_atts( array(
                'post_id' => '',                                        
            ), $atts );
            
            $post_id = $params["post_id"];
            if ($post_id != "")
            {
                $post_id = intval($post_id);
                wp_publish_post($post_id);
                return "Post published:$post_id";
            }
        }


        public function get_tech_posts()
        {

            $options = get_option( TECH_UPDATE_SETTINGS );
            $category = $options[TECH_UPDATE_FEED];
            $post_status = $options[TECH_UPDATE_POST_STATUS];
            
            if ($category == "0") { return "No posts imported. Please select a feed."; }

            if (!isset($category) || $category == "") { $category = 8; }

            $email = $options[TECH_UPDATE_LICENSE];
            $licensed = YOUR_TECH_UPDATES::check_license($email);
            if ($licensed == false) {
                return;
            }

            $time = strtotime("now");
            $uid = $options[TECH_UPDATE_LICENSE];
            //$url = 'https://www.yourtechupdates.com/wp-json/wp/v2/posts/?categories='.$category.'&uid='.$uid.'&time='.$time;
            $url = "https://www.yourtechupdates.com/wp-json/tech/videos/?categories=$category&uid=$uid&time=$time";
            $response = wp_remote_get( $url, array('sslverify' => FALSE) );
    
            if ( is_array( $response ) && ! is_wp_error( $response ) ) 
            {
                $headers = $response['headers']; // array of http header lines
                $body    = $response['body']; // use the content
                $content = json_decode($body);
                $status = "";
                $updated_posts = false;
                $update_post_count = 0;
                foreach ($content as $article)
                {
                    $send_update_acf = false;
                    $send_update_ver_acf = 0;
                    $week_commencing = "";

                    if (isset($article->acf->week_commencing)) {
                        $week_commencing = $article->acf->week_commencing;
                    }

                    if (isset($article->acf->send_update)) {
                        $send_update_acf = $article->acf->send_update;
                    }
                    if (isset($article->acf->update_version)){
                        $send_update_ver_acf = $article->acf->update_version;
                    }

                    $yourtech_postid = $article->id;
                    $title = $article->title->rendered;
                    $slug = $article->slug;
                    $date = $article->date;
                    $link = $article->link;
                    $text = $article->content->rendered;
                    $excerpt = $article->excerpt->rendered;
                    $excerpt = wp_strip_all_tags(preg_replace("/<\/?a( [^>]*)?>/i", "", $excerpt));

                    $image = $article->yoast_head_json->og_image[0]->url;
                    $guid = $article->guid->rendered;
                    $post_actual_status = $article->status;
                    if ($post_actual_status == "future" && $post_status == "publish")
                    {
                        $post_status = "future";
                    }

                    $yourtech_parent_postid = 0;
                    if (isset($article->acf->parent_page_url))
                    {
                        $yourtech_parent_postid = $article->acf->parent_page_url;
                    }

                    if (!$this::post_exists($guid))
                    {
                        $status .= $this::create_post($title, $slug, $date, $link, $text, $excerpt, $image, $post_status,$category, $guid, $yourtech_postid, $yourtech_parent_postid, $week_commencing);
                    }
                    else
                    {
                        if ($send_update_acf == true) {
                            $updated_post_content = $this::update_post($title, $slug, $date, $link, $text, $excerpt, $image, $post_status,$category, $send_update_ver_acf, $guid, $yourtech_postid, $yourtech_parent_postid, $week_commencing);
                            $status .= $updated_post_content;
                            if ( $updated_post_content != "") { $updated_posts = true; $update_post_count++; }
                        }
                    }
                }
                if ($updated_posts)
                {
                    $status .= "<p style='color:red; font-weight:bold;'>We have updated ".$update_post_count." post(s). We're sorry for the inconvenience caused. We don't intend to overwrite existing posts regularly. </p>";
                }

                $this::sendEmail($status);
                return $status;
            }
        }

        public function create_post($title, $slug, $date, $link, $text, $excerpt, $image, $post_status, $category, $guid, $yourtech_postid, $yourtech_parent_postid, $week_commencing)
        {
            if ( ! function_exists( 'post_exists' ) ) {
                require_once( ABSPATH . 'wp-admin/includes/post.php' );
            }

            //$found_post = $this::post_exists($guid);

            //if ($found_post) {
            //    return "";
            //}
            $admin_id = $this::get_main_admin_id();
            $author_id = get_option('your_tech_updates_author');
            if ($author_id == "" || $author_id == null)
            {
                $author_id = $admin_id;
            }


            $getImageFile = $image;
            kses_remove_filters();
            $my_post  = array( 'post_title'     => $title,
                   'post_type'      => 'post',
                   'post_name'      => $slug,
                   'post_content'   => $text,
                   'post_excerpt'   => $excerpt,
                   'post_date'      => $date,
                   'guid'           => $guid,
                   'post_status'    => $post_status,
                   'comment_status' => 'closed',
                   'ping_status'    => 'closed',
                   'post_author'    => $author_id,
                   'menu_order'     => 0 );

            remove_action('save_post',  array($this,'save_post'), 10, 3);
            $post_id = wp_insert_post( $my_post, FALSE ); //
            add_action('save_post',  array($this,'save_post'), 10, 3);

            kses_init_filters();

            $update_version = 0;
            add_post_meta($post_id, YOUR_TECH_UPDATE_VERSION, $update_version);

            $cat_id = get_option('your_tech_updates_category');
            if ($cat_id != "")
                wp_set_post_categories($post_id, $cat_id, false);

            if($post_id != 0)
            {
                $canonical = $this::get_parent_post_url($yourtech_parent_postid, $link);
                add_post_meta($post_id, 'edge_url', $canonical);
                add_post_meta($post_id, 'edge_post_id', $yourtech_postid);
                add_post_meta($post_id, 'edge_parent_post_id', $yourtech_parent_postid);
                add_post_meta($post_id, 'mspedge_week_commencing', $week_commencing);

                if ($category == "5" || $category == "6" || $category == "7")
                {
                    add_post_meta($post_id, 'edge_has_video', "true");
                }
            }
            
            $this::image_upload($post_id, $image, $title);
                
            if ($post_status == "publish"){
                wp_publish_post($post_id);
            }

            $post = get_post($post_id);
            $this::upload_social_feed($post_id, $post, "Feed Create");
            

            wp_set_current_user($admin_id);
            $edit_link = get_edit_post_link($post_id);
            wp_set_current_user(0);
            
            return "<p><strong>Imported:</strong><br/><a href='$edit_link'>$title</a><br/>Publish date:$date</p>";

        }

        public function update_post($title, $slug, $date, $link, $text, $excerpt, $image, $post_status, $category, $update_version, $guid, $yourtech_postid, $yourtech_parent_postid, $week_commencing)
        {
            if ( ! function_exists( 'post_exists' ) ) {
                require_once( ABSPATH . 'wp-admin/includes/post.php' );
            }

            $found_post_id = $this::post_exists($guid);

            if ($found_post_id == 0) {
                return "";
            }

            //Only continue and update post if the version number is different to the previous one.
            $edge_update_version = get_post_custom_values( YOUR_TECH_UPDATE_VERSION, $found_post_id );
            if ($edge_update_version != null)
            {
                if (count($edge_update_version) > 0) {
                    $edge_update_version_value = $edge_update_version[0];
                    if (intval($edge_update_version_value) == intval($update_version)) {
                        return "";
                    }
                }
            }

            $getImageFile = $image;
            kses_remove_filters();
            $my_post  = array( 'post_title'     => $title,
                   'ID'             => $found_post_id,
                   'post_type'      => 'post',
                   'post_name'      => $slug,
                   'post_content'   => $text,
                   'post_excerpt'   => $excerpt,
                   'post_date'      => $date,
                   'post_status'    => $post_status,
                   'menu_order'     => 0 );

            remove_action('save_post',  array($this,'save_post'), 10, 3);
            $post_id = wp_update_post( $my_post, FALSE );
            add_action('save_post',  array($this,'save_post'), 10, 3);

            kses_init_filters();

            update_post_meta($post_id, YOUR_TECH_UPDATE_VERSION, $update_version);
            update_post_meta($post_id, 'mspedge_week_commencing', $week_commencing);

            if($post_id != 0)
            {
                $canonical = $this::get_parent_post_url($yourtech_parent_postid, $link);
                update_post_meta($post_id, 'edge_url', $canonical);
                update_post_meta($post_id, 'edge_parent_post_id', $yourtech_parent_postid);
                if ($category == "5" || $category == "6" || $category == "7")
                {
                    update_post_meta($post_id, 'edge_has_video', "true");
                }
            }

            include_once( ABSPATH . 'wp-admin/includes/image.php' );

            $this::image_upload($post_id, $image, $title);

            if ($post_status == "publish"){
                wp_publish_post($post_id);
            }
            
            $post = get_post($post_id);
            $this::upload_social_feed($post_id, $post, "Feed Update");

            $admin_id = $this::get_main_admin_id();
            wp_set_current_user($admin_id);

            $edit_link = get_edit_post_link($post_id);
            wp_set_current_user(0);
            
            return "<p><strong>Updated:</strong><br/><a href='$edit_link'>$title</a><br/>Publish date:$date</p>";

        }

        function get_parent_post_url($parent_post_id, $default_link)
        {
            if ($parent_post_id == 0) { return $default_link; }

            $time = strtotime("now");
            $link = $default_link;

            //$response = wp_remote_get( 'https://www.yourtechupdates.com/wp-json/wp/v2/posts/'.$parent_post_id, array('sslverify' => FALSE) );
            $response = wp_remote_get( "https://www.yourtechupdates.com/wp-json/tech/videos/$parent_post_id", array('sslverify' => FALSE) );

            if ( is_array( $response ) && ! is_wp_error( $response ) ) 
            {
                $headers = $response['headers']; // array of http header lines
                $body    = $response['body']; // use the content
                $content = json_decode($body);
                $link = $content->link;
            }
            return $link;
        }

        function get_main_admin_id()
        {
            $users_query = new WP_User_Query( array( 'role' => 'administrator', 'orderby' => 'ID'));  
            $results = $users_query->get_results();
            if (count($results) > 0) {
                return $results[0]->ID;
            }
            else {
                return 1;
            }
        }

        function post_exists( $guid ) {
            global $wpdb;

            $guid   = wp_unslash( sanitize_post_field( 'guid', $guid, 0, 'db' ) );

            $query = "SELECT ID FROM $wpdb->posts WHERE 1=1";
            $args  = array();


            if ( ! empty( $guid ) ) {
                $query .= ' AND guid = %s';
                $args[] = $guid;
            }

            if ( ! empty( $args ) ) {
                return (int) $wpdb->get_var( $wpdb->prepare( $query, $args ) );
            }
            return 0;
        }


        function processSchedule ( $schedules ) {
            $schedules['four_times_per_day_tech_updates'] = array(
                'interval' => 21600, // Four times a Day
                'display'  => __( 'Import Tech Updates Check 4 Times a Day' ),
            );
            return $schedules;
        }
        
        function sendEmail($posts)
        {
            $options = get_option( TECH_UPDATE_SETTINGS );
            $toemail = $options[TECH_UPDATE_NOTIFICATION_EMAIL];
            $post_status = $options[TECH_UPDATE_POST_STATUS];
            if ($toemail == "" || $posts == "") { return; }
            
            $subjectDraft = "MSP Marketing Edge AutoBlog - new DRAFT blog post(s)";
            $subjectPublished = "MSP Marketing Edge AutoBlog - new blog post(s)";
            $subject = $subjectDraft;
            
            $textDraft = "<p>You have the following new post(s) in DRAFT mode to read and set live.</p>";
            $textPublished = "<p>The following new post(s) have automatically gone live on your website.</p>";
            $textInsert = $textDraft;
            if ($post_status == "publish") { $textInsert = $textPublished; $subject = $subjectPublished; }
            
            $body = "";
            $body .= "<p>This is an automatic notification from the MSP Marketing Edge AutoBlog wordpress plugin.</p>";
            $body .= $textInsert;
            $body .= $posts;
            $body .= "<p>Remember you can adjust your publication settings within the MSP Marketing Edge AutoBlog plugin. For support regarding the plugin, visit  <a href='https://portal.mspmarketingedge.com/autoblog/'>portal.mspmarketingedge.com/autoblog/</a></p>";
            $body .= "<p>The MSP Marketing Edge team.</p>";
            
            $headers = array('Content-Type: text/html; charset=UTF-8');
            wp_mail( $toemail, $subject, $body, $headers );
        }

    } 
    $YOUR_TECH_UPDATES = new YOUR_TECH_UPDATES();    
}

//Handle Yoast limitation where the OG:Image isn't added unless manually published.
function alter_existing_opengraph_image( $image ) {
    global $post;
    //Check we can access post.
    if($post == null) { return;}
    //Make sure we only do this on AutoBlog posts.
    $values = get_post_custom_values( 'edge_url', $post->ID );
    if($values == null) { return;}
    $post_id = $post->ID;
    //Get post feature image.
    if ( is_single( $post_id ) ) {
        $image = wp_get_attachment_url( get_post_thumbnail_id($post_id) );
        if ($image != false)
           return $image; 
    }
    return $image;
}
add_filter( 'wpseo_opengraph_image', 'alter_existing_opengraph_image' );

